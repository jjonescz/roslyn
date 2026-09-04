// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal abstract class CSharpCompiler : CommonCompiler
    {
        internal const string ResponseFileName = "csc.rsp";

        private readonly CommandLineDiagnosticFormatter _diagnosticFormatter;
        private readonly string? _tempDirectory;

        protected CSharpCompiler(CSharpCommandLineParser parser, string? responseFile, string[] args, BuildPaths buildPaths, string? additionalReferenceDirectories, IAnalyzerAssemblyLoader assemblyLoader, GeneratorDriverCache? driverCache = null, ICommonCompilerFileSystem? fileSystem = null)
            : base(parser, responseFile, args, buildPaths, additionalReferenceDirectories, assemblyLoader, driverCache, fileSystem)
        {
            _diagnosticFormatter = new CommandLineDiagnosticFormatter(buildPaths.WorkingDirectory, Arguments.PrintFullPaths, Arguments.ShouldIncludeErrorEndLocation);
            _tempDirectory = buildPaths.TempDirectory;
        }

        public override DiagnosticFormatter DiagnosticFormatter { get { return _diagnosticFormatter; } }
        protected internal new CSharpCommandLineArguments Arguments { get { return (CSharpCommandLineArguments)base.Arguments; } }

        protected virtual CSharpCompilation? TryGetPreviousCompilation()
            => null;

        protected virtual void OnInputCompilationCreated(
            CSharpCompilation compilation,
            bool reusedCompilation,
            int reusedSyntaxTreeCount,
            int totalSyntaxTreeCount,
            long updateMilliseconds)
        {
        }

        public override Compilation? CreateCompilation(
            TextWriter consoleOutput,
            TouchedFileLogger? touchedFilesLogger,
            ErrorLogger? errorLogger,
            ImmutableArray<AnalyzerConfigOptionsResult> analyzerConfigOptions,
            AnalyzerConfigOptionsResult globalConfigOptions)
        {
            var parseOptions = Arguments.ParseOptions;

            // We compute script parse options once so we don't have to do it repeatedly in
            // case there are many script files.
            var scriptParseOptions = parseOptions.WithKind(SourceCodeKind.Script);

            bool hadErrors = false;

            var sourceFiles = Arguments.SourceFiles;
            var trees = new SyntaxTree?[sourceFiles.Length];
            var normalizedFilePaths = new string?[sourceFiles.Length];
            var diagnosticBag = DiagnosticBag.GetInstance();
            var previousCompilation = TryGetPreviousCompilation();
            var previousTreesByPath = CreatePreviousSyntaxTreeMap(previousCompilation);
            var reusedSyntaxTrees = new bool[sourceFiles.Length];

            if (Arguments.CompilationOptions.ConcurrentBuild)
            {
                RoslynParallel.For(
                    0,
                    sourceFiles.Length,
                    UICultureUtilities.WithCurrentUICulture<int>(i =>
                    {
                        //NOTE: order of trees is important!!
                        trees[i] = ParseFile(
                            parseOptions,
                            scriptParseOptions,
                            ref hadErrors,
                            sourceFiles[i],
                            diagnosticBag,
                            out normalizedFilePaths[i],
                            previousTreesByPath,
                            reusedSyntaxTrees,
                            i);
                    }),
                    CancellationToken.None);
            }
            else
            {
                for (int i = 0; i < sourceFiles.Length; i++)
                {
                    //NOTE: order of trees is important!!
                    trees[i] = ParseFile(
                        parseOptions,
                        scriptParseOptions,
                        ref hadErrors,
                        sourceFiles[i],
                        diagnosticBag,
                        out normalizedFilePaths[i],
                        previousTreesByPath,
                        reusedSyntaxTrees,
                        i);
                }
            }

            // If errors had been reported in ParseFile, while trying to read files, then we should simply exit.
            if (ReportDiagnostics(diagnosticBag.ToReadOnlyAndFree(), consoleOutput, errorLogger, compilation: null))
            {
                Debug.Assert(hadErrors);
                return null;
            }

            var diagnostics = new List<DiagnosticInfo>();
            var uniqueFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                var normalizedFilePath = normalizedFilePaths[i];
                Debug.Assert(normalizedFilePath != null);
                Debug.Assert(sourceFiles[i].IsInputRedirected || PathUtilities.IsAbsolute(normalizedFilePath));

                if (!uniqueFilePaths.Add(normalizedFilePath))
                {
                    // warning CS2002: Source file '{0}' specified multiple times
                    diagnostics.Add(new DiagnosticInfo(MessageProvider, (int)ErrorCode.WRN_FileAlreadyIncluded,
                        Arguments.PrintFullPaths ? normalizedFilePath : _diagnosticFormatter.RelativizeNormalizedPath(normalizedFilePath)));

                    trees[i] = null;
                }
            }

            if (Arguments.TouchedFilesPath != null)
            {
                Debug.Assert(touchedFilesLogger is object);
                foreach (var path in uniqueFilePaths)
                {
                    touchedFilesLogger.AddRead(path);
                }
            }

            var assemblyIdentityComparer = DesktopAssemblyIdentityComparer.Default;
            var appConfigPath = this.Arguments.AppConfigPath;
            if (appConfigPath != null)
            {
                try
                {
                    using (var appConfigStream = new FileStream(appConfigPath, FileMode.Open, FileAccess.Read))
                    {
                        assemblyIdentityComparer = DesktopAssemblyIdentityComparer.LoadFromXml(appConfigStream);
                    }

                    if (touchedFilesLogger != null)
                    {
                        touchedFilesLogger.AddRead(appConfigPath);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new DiagnosticInfo(MessageProvider, (int)ErrorCode.ERR_CantReadConfigFile, appConfigPath, ex.Message));
                }
            }

            var xmlFileResolver = new LoggingXmlFileResolver(Arguments.BaseDirectory, touchedFilesLogger);
            var sourceFileResolver = new LoggingSourceFileResolver(ImmutableArray<string>.Empty, Arguments.BaseDirectory, Arguments.PathMap, touchedFilesLogger);

            MetadataReferenceResolver referenceDirectiveResolver;
            var resolvedReferences = ResolveMetadataReferences(diagnostics, touchedFilesLogger, out referenceDirectiveResolver);
            if (ReportDiagnostics(diagnostics, consoleOutput, errorLogger, compilation: null))
            {
                return null;
            }

            var loggingFileSystem = new LoggingStrongNameFileSystem(touchedFilesLogger, _tempDirectory);
            var optionsProvider = new CompilerSyntaxTreeOptionsProvider(trees, analyzerConfigOptions, globalConfigOptions);
            var compilationOptions = Arguments.CompilationOptions
                .WithMetadataReferenceResolver(referenceDirectiveResolver)
                .WithAssemblyIdentityComparer(assemblyIdentityComparer)
                .WithXmlReferenceResolver(xmlFileResolver)
                .WithStrongNameProvider(Arguments.GetStrongNameProvider(loggingFileSystem))
                .WithSourceReferenceResolver(sourceFileResolver)
                .WithSyntaxTreeOptionsProvider(optionsProvider);
            var finalTrees = trees.WhereNotNull().ToImmutableArray();
            var reusedSyntaxTreeCount = 0;
            for (var i = 0; i < trees.Length; i++)
            {
                if (trees[i] is object && reusedSyntaxTrees[i])
                {
                    reusedSyntaxTreeCount++;
                }
            }

            var updateStopwatch = Stopwatch.StartNew();
            var (compilation, reusedCompilation) = CreateOrUpdateCompilation(
                previousCompilation,
                Arguments.CompilationName,
                finalTrees,
                resolvedReferences,
                compilationOptions,
                reusedSyntaxTreeCount);
            updateStopwatch.Stop();

            OnInputCompilationCreated(
                compilation,
                reusedCompilation,
                reusedSyntaxTreeCount,
                finalTrees.Length,
                updateStopwatch.ElapsedMilliseconds);
            return compilation;
        }

        private SyntaxTree? ParseFile(
            CSharpParseOptions parseOptions,
            CSharpParseOptions scriptParseOptions,
            ref bool addedDiagnostics,
            CommandLineSourceFile file,
            DiagnosticBag diagnostics,
            out string? normalizedFilePath,
            Dictionary<string, SyntaxTree>? previousTreesByPath,
            bool[] reusedSyntaxTrees,
            int sourceFileIndex)
        {
            var fileDiagnostics = new List<DiagnosticInfo>();
            var content = TryReadFileContent(file, fileDiagnostics, out normalizedFilePath);

            if (content == null)
            {
                foreach (var info in fileDiagnostics)
                {
                    diagnostics.Add(MessageProvider.CreateDiagnostic(info));
                }
                fileDiagnostics.Clear();
                addedDiagnostics = true;
                return null;
            }
            else
            {
                Debug.Assert(fileDiagnostics.Count == 0);
                var treeOptions = file.IsScript ? scriptParseOptions : parseOptions;
                if (previousTreesByPath is object &&
                    previousTreesByPath.TryGetValue(file.Path, out var previousTree) &&
                    previousTree.Options.Equals(treeOptions) &&
                    CanReuseSourceText(previousTree.GetText(), content, EmbeddedSourcePaths.Contains(file.Path)))
                {
                    reusedSyntaxTrees[sourceFileIndex] = true;
                    return previousTree;
                }

                return ParseFile(parseOptions, scriptParseOptions, content, file);
            }
        }

        private static bool CanReuseSourceText(SourceText previous, SourceText current, bool mustBeEmbeddable)
            => previous.ContentEquals(current) &&
               previous.ChecksumAlgorithm == current.ChecksumAlgorithm &&
               Equals(previous.Encoding, current.Encoding) &&
               previous.GetChecksum().SequenceEqual(current.GetChecksum()) &&
               (!mustBeEmbeddable || previous.CanBeEmbedded);

        private static Dictionary<string, SyntaxTree>? CreatePreviousSyntaxTreeMap(CSharpCompilation? compilation)
        {
            if (compilation is null)
            {
                return null;
            }

            var result = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
            foreach (var tree in compilation.SyntaxTrees)
            {
                result.Add(tree.FilePath, tree);
            }

            return result;
        }

        private static (CSharpCompilation compilation, bool reusedCompilation) CreateOrUpdateCompilation(
            CSharpCompilation? previousCompilation,
            string? assemblyName,
            ImmutableArray<SyntaxTree> syntaxTrees,
            List<MetadataReference> references,
            CSharpCompilationOptions options,
            int reusedSyntaxTreeCount)
        {
            if (previousCompilation is null ||
                MayHaveReferenceOrLoadDirectives(syntaxTrees) ||
                !HaveEquivalentCompilationOptions(previousCompilation.Options, options))
            {
                return (CSharpCompilation.Create(assemblyName, syntaxTrees, references, options), false);
            }

            var compilation = previousCompilation;
            if (!string.Equals(compilation.AssemblyName, assemblyName, StringComparison.Ordinal))
            {
                compilation = compilation.WithAssemblyName(assemblyName);
            }

            compilation = compilation.WithOptions(
                options
                    .WithMetadataReferenceResolver(compilation.Options.MetadataReferenceResolver)
                    .WithSourceReferenceResolver(compilation.Options.SourceReferenceResolver));

            // Resolve references for every request so a file replaced at the same path cannot leave
            // the updated compilation bound to metadata observed by the previous request.
            compilation = compilation.WithReferences(references);

            return (WithSyntaxTrees(compilation, syntaxTrees, reusedSyntaxTreeCount), true);
        }

        private static bool MayHaveReferenceOrLoadDirectives(ImmutableArray<SyntaxTree> syntaxTrees)
        {
            foreach (var tree in syntaxTrees)
            {
                if (tree.HasReferenceOrLoadDirectives())
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HaveEquivalentCompilationOptions(
            CSharpCompilationOptions previousOptions,
            CSharpCompilationOptions options)
        {
            if (previousOptions.SyntaxTreeOptionsProvider is not CompilerSyntaxTreeOptionsProvider previousProvider ||
                options.SyntaxTreeOptionsProvider is not CompilerSyntaxTreeOptionsProvider provider ||
                !previousProvider.IsEquivalentTo(provider))
            {
                return false;
            }

            var normalizedOptions = options
                .WithCurrentLocalTime(previousOptions.CurrentLocalTime)
                .WithMetadataReferenceResolver(previousOptions.MetadataReferenceResolver)
                .WithSyntaxTreeOptionsProvider(previousOptions.SyntaxTreeOptionsProvider);
            return previousOptions.Equals(normalizedOptions);
        }

        private static CSharpCompilation WithSyntaxTrees(
            CSharpCompilation compilation,
            ImmutableArray<SyntaxTree> syntaxTrees,
            int reusedSyntaxTreeCount)
        {
            var previousTrees = compilation.SyntaxTrees;
            if (previousTrees.Length != syntaxTrees.Length ||
                reusedSyntaxTreeCount * 2 < syntaxTrees.Length)
            {
                return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(syntaxTrees);
            }

            for (var i = 0; i < syntaxTrees.Length; i++)
            {
                if (!string.Equals(previousTrees[i].FilePath, syntaxTrees[i].FilePath, StringComparison.Ordinal))
                {
                    return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(syntaxTrees);
                }
            }

            for (var i = 0; i < syntaxTrees.Length; i++)
            {
                if (!ReferenceEquals(previousTrees[i], syntaxTrees[i]))
                {
                    compilation = compilation.ReplaceSyntaxTree(previousTrees[i], syntaxTrees[i]);
                }
            }

            return compilation;
        }

        private static SyntaxTree ParseFile(
            CSharpParseOptions parseOptions,
            CSharpParseOptions scriptParseOptions,
            SourceText content,
            CommandLineSourceFile file)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(
                content,
                file.IsScript ? scriptParseOptions : parseOptions,
                file.Path);

            // prepopulate line tables.
            // we will need line tables anyways and it is better to not wait until we are in emit
            // where things run sequentially.
            bool isHiddenDummy;
            tree.GetMappedLineSpanAndVisibility(default(TextSpan), out isHiddenDummy);

            return tree;
        }

        /// <summary>
        /// Given a compilation and a destination directory, determine three names:
        ///   1) The name with which the assembly should be output.
        ///   2) The path of the assembly/module file.
        ///   3) The path of the pdb file.
        ///
        /// When csc produces an executable, but the name of the resulting assembly
        /// is not specified using the "/out" switch, the name is taken from the name
        /// of the file (note: file, not class) containing the assembly entrypoint
        /// (as determined by binding and the "/main" switch).
        ///
        /// For example, if the command is "csc /target:exe a.cs b.cs" and b.cs contains the
        /// entrypoint, then csc will produce "b.exe" and "b.pdb" in the output directory,
        /// with assembly name "b" and module name "b.exe" embedded in the file.
        /// </summary>
        protected override string GetOutputFileName(Compilation compilation, CancellationToken cancellationToken)
        {
            if (Arguments.OutputFileName is object)
            {
                return Arguments.OutputFileName;
            }

            Debug.Assert(Arguments.CompilationOptions.OutputKind.IsApplication());

            var comp = (CSharpCompilation)compilation;

            Symbol? entryPoint = comp.ScriptClass;
            if (entryPoint is null)
            {
                var method = comp.GetEntryPoint(cancellationToken);
                if (method is object)
                {
                    entryPoint = method.PartialImplementationPart ?? method;
                }
                else
                {
                    // no entrypoint found - an error will be reported and the compilation won't be emitted
                    return "error";
                }
            }

            string entryPointFileName = PathUtilities.GetFileName(entryPoint.GetFirstLocation().SourceTree!.FilePath);
            return Path.ChangeExtension(entryPointFileName, ".exe");
        }

        internal override bool SuppressDefaultResponseFile(IEnumerable<string> args)
        {
            return args.Any(arg => new[] { "/noconfig", "-noconfig" }.Contains(arg.ToLowerInvariant()));
        }

        /// <summary>
        /// Print compiler logo
        /// </summary>
        /// <param name="consoleOutput"></param>
        public override void PrintLogo(TextWriter consoleOutput)
        {
            consoleOutput.WriteLine(ErrorFacts.GetMessage(MessageID.IDS_LogoLine1, Culture), GetToolName(), GetCompilerVersion());
            consoleOutput.WriteLine(ErrorFacts.GetMessage(MessageID.IDS_LogoLine2, Culture));
            consoleOutput.WriteLine();
        }

        public override void PrintLangVersions(TextWriter consoleOutput)
        {
            consoleOutput.WriteLine(ErrorFacts.GetMessage(MessageID.IDS_LangVersions, Culture));
            var defaultVersion = LanguageVersion.Default.MapSpecifiedToEffectiveVersion();
            var latestVersion = LanguageVersion.Latest.MapSpecifiedToEffectiveVersion();
            foreach (var v in (LanguageVersion[])Enum.GetValues(typeof(LanguageVersion)))
            {
                if (v == defaultVersion)
                {
                    consoleOutput.WriteLine($"{v.ToDisplayString()} (default)");
                }
                else if (v == latestVersion)
                {
                    consoleOutput.WriteLine($"{v.ToDisplayString()} (latest)");
                }
                else
                {
                    consoleOutput.WriteLine(v.ToDisplayString());
                }
            }
            consoleOutput.WriteLine();
        }

        internal override Type Type
        {
            get
            {
                // We do not use this.GetType() so that we don't break mock subtypes
                return typeof(CSharpCompiler);
            }
        }

        internal override string GetToolName()
        {
            return ErrorFacts.GetMessage(MessageID.IDS_ToolName, Culture);
        }

        /// <summary>
        /// Print Commandline help message (up to 80 English characters per line)
        /// </summary>
        /// <param name="consoleOutput"></param>
        public override void PrintHelp(TextWriter consoleOutput)
        {
            consoleOutput.WriteLine(ErrorFacts.GetMessage(MessageID.IDS_CSCHelp, Culture));
        }

        protected override bool TryGetCompilerDiagnosticCode(string diagnosticId, out uint code)
        {
            return CommonCompiler.TryGetCompilerDiagnosticCode(diagnosticId, "CS", out code);
        }

        protected override void ResolveAnalyzersFromArguments(
            List<DiagnosticInfo> diagnostics,
            CommonMessageProvider messageProvider,
            CompilationOptions compilationOptions,
            bool skipAnalyzers,
            out ImmutableArray<DiagnosticAnalyzer> analyzers,
            out ImmutableArray<ISourceGenerator> generators)
        {
            Arguments.ResolveAnalyzersFromArguments(LanguageNames.CSharp, diagnostics, messageProvider, AssemblyLoader, compilationOptions, skipAnalyzers, out analyzers, out generators);
        }

        protected override void ResolveEmbeddedFilesFromExternalSourceDirectives(
            SyntaxTree tree,
            SourceReferenceResolver resolver,
            OrderedSet<string> embeddedFiles,
            DiagnosticBag diagnostics)
        {
            foreach (LineDirectiveTriviaSyntax directive in tree.GetRoot().GetDirectives(
                d => d.IsActive && !d.HasErrors && d.Kind() == SyntaxKind.LineDirectiveTrivia))
            {
                var path = (string?)directive.File.Value;
                if (path == null)
                {
                    continue;
                }

                string? resolvedPath = resolver.ResolveReference(path, tree.FilePath);
                if (resolvedPath == null)
                {
                    diagnostics.Add(
                        MessageProvider.CreateDiagnostic(
                            (int)ErrorCode.ERR_NoSourceFile,
                            directive.File.GetLocation(),
                            path,
                            CSharpResources.CouldNotFindFile));

                    continue;
                }

                embeddedFiles.Add(resolvedPath);
            }
        }

        private protected override GeneratorDriver CreateGeneratorDriver(string baseDirectory, ParseOptions parseOptions, ImmutableArray<ISourceGenerator> generators, AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, ImmutableArray<AdditionalText> additionalTexts, SourceHashAlgorithm checksumAlgorithm)
        {
            return CSharpGeneratorDriver.Create(generators, additionalTexts, (CSharpParseOptions)parseOptions, analyzerConfigOptionsProvider, driverOptions: new GeneratorDriverOptions(disabledOutputs: IncrementalGeneratorOutputKind.Host, baseDirectory: baseDirectory) { ChecksumAlgorithm = checksumAlgorithm });
        }

        private protected override void DiagnoseBadAccesses(TextWriter consoleOutput, ErrorLogger? errorLogger, Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
        {
            DiagnosticBag newDiagnostics = DiagnosticBag.GetInstance();
            foreach (var diag in diagnostics)
            {
                var symbol = diag switch
                {
                    { Code: (int)ErrorCode.ERR_BadAccess, Arguments: [Symbol s] } => s,
                    { Code: (int)ErrorCode.ERR_InaccessibleGetter, Arguments: [Symbol s] } => s,
                    { Code: (int)ErrorCode.ERR_InaccessibleSetter, Arguments: [Symbol s] } => s,
                    { Code: (int)ErrorCode.ERR_ImplicitImplementationOfInaccessibleInterfaceMember, Arguments: [_, Symbol s, _] } => s,
                    _ => null
                };

                if (symbol is null || ReferenceEquals(compilation.Assembly, symbol.ContainingAssembly))
                {
                    // Can't be IVT related
                    continue;
                }

                // '{0}' is defined in assembly '{1}'.
                newDiagnostics.Add(new CSDiagnostic(
                    new CSDiagnosticInfo(ErrorCode.ERR_SymbolDefinedInAssembly, symbol, symbol.ContainingAssembly),
                    diag.Location));
            }

            ReportDiagnostics(newDiagnostics.ToReadOnlyAndFree(), consoleOutput, errorLogger, compilation);
        }
    }
}
