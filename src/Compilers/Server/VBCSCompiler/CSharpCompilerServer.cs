// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis.CommandLine;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    internal sealed class CSharpCompilerServer : CSharpCompiler, ICompilerServerTelemetryProvider
    {
        private readonly Func<string, MetadataReferenceProperties, PortableExecutableReference> _metadataProvider;
        private readonly CompilationCache? _cache;
        private readonly CompilationCacheTelemetry _cacheTelemetry = new CompilationCacheTelemetry();
        private readonly CSharpCompilationCache? _compilationCache;
        private readonly IncrementalCompilationTelemetry _incrementalCompilationTelemetry;
        private readonly ICompilerServerLogger _logger;
        private readonly string? _compilationCacheKey;
        private CSharpCompilation? _inputCompilation;

        internal CSharpCompilerServer(Func<string, MetadataReferenceProperties, PortableExecutableReference> metadataProvider, string[] args, BuildPaths buildPaths, string? libDirectory, IAnalyzerAssemblyLoader analyzerLoader, GeneratorDriverCache driverCache, CSharpCompilationCache? compilationCache = null, ICompilerServerLogger? logger = null)
            : this(metadataProvider, Path.Combine(buildPaths.ClientDirectory, ResponseFileName), args, buildPaths, libDirectory, analyzerLoader, driverCache, compilationCache, logger)
        {
        }

        internal CSharpCompilerServer(Func<string, MetadataReferenceProperties, PortableExecutableReference> metadataProvider, string? responseFile, string[] args, BuildPaths buildPaths, string? libDirectory, IAnalyzerAssemblyLoader analyzerLoader, GeneratorDriverCache driverCache, CSharpCompilationCache? compilationCache = null, ICompilerServerLogger? logger = null)
            : base(CSharpCommandLineParser.Default, responseFile, args, buildPaths, libDirectory, analyzerLoader, driverCache)
        {
            _metadataProvider = metadataProvider;
            _logger = logger ?? EmptyCompilerServerLogger.Instance;
            _cache = CompilationCache.TryCreate(Arguments, _logger);
            _compilationCache = compilationCache;
            _incrementalCompilationTelemetry = new IncrementalCompilationTelemetry(_logger);
            _compilationCacheKey = string.IsNullOrWhiteSpace(Arguments.OutputFileName) ||
                Arguments.TouchedFilesPath is object ||
                Arguments.AppConfigPath is object
                ? null
                : Arguments.GetOutputFilePath(Arguments.OutputFileName);
        }

        internal override Func<string, MetadataReferenceProperties, PortableExecutableReference> GetMetadataProvider()
        {
            return _metadataProvider;
        }

        protected override int? CheckCache(
            Compilation compilation,
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            ImmutableArray<ISourceGenerator> generators,
            ImmutableArray<AdditionalText> additionalTexts,
            CancellationToken cancellationToken,
            out object? cacheState)
        {
            var result = CompilationCacheUtilities.CheckCache(_cache, _logger, Arguments, compilation, analyzers, generators, additionalTexts, _cacheTelemetry, cancellationToken, out var deterministicKey, out var hashKey);
            cacheState = (deterministicKey, hashKey);
            return result;
        }

        protected override void OnCompilationStarted()
        {
            _cacheTelemetry.StartCompileTimer();
            if (_compilationCacheKey is object)
            {
                _incrementalCompilationTelemetry.StartCompileAndEmit();
            }
        }

        protected override void OnCompilationCompleted(bool succeeded)
        {
            _cacheTelemetry.StopCompileTimer(succeeded);
            if (_compilationCacheKey is object)
            {
                _incrementalCompilationTelemetry.Complete(succeeded);
            }
        }

        protected override CSharpCompilation? TryGetPreviousCompilation()
            => _compilationCacheKey is null
                ? null
                : _compilationCache?.TryGetCompilation(_compilationCacheKey);

        protected override void OnInputCompilationCreated(
            CSharpCompilation compilation,
            bool reusedCompilation,
            int reusedSyntaxTreeCount,
            int totalSyntaxTreeCount,
            long updateMilliseconds)
        {
            if (_compilationCacheKey is null)
            {
                return;
            }

            _inputCompilation = compilation;
            _incrementalCompilationTelemetry.RecordCompilationReuse(
                reusedCompilation,
                reusedSyntaxTreeCount,
                totalSyntaxTreeCount,
                updateMilliseconds);
        }

        protected override void OnCompilationCreated(long elapsedMilliseconds)
        {
            if (_compilationCacheKey is object)
            {
                _incrementalCompilationTelemetry.RecordCompilationCreation(elapsedMilliseconds);
            }
        }

        protected override void OnCompileMethodsCompleted(long elapsedMilliseconds)
        {
            if (_compilationCacheKey is object)
            {
                _incrementalCompilationTelemetry.RecordCompileMethods(elapsedMilliseconds);
            }
        }

        protected override void OnSerializationCompleted(long elapsedMilliseconds)
        {
            if (_compilationCacheKey is object)
            {
                _incrementalCompilationTelemetry.RecordSerialization(elapsedMilliseconds);
            }
        }

        protected override void OnCompilationResultRestoredFromCache(Compilation compilation)
        {
            if (_compilationCacheKey is object && _inputCompilation is object)
            {
                _compilationCache?.CacheCompilation(_compilationCacheKey, _inputCompilation.RemoveAllReferences());
                _incrementalCompilationTelemetry.CompleteFromOutputCache();
            }
        }

        protected override void OnCompilationSucceeded(
            Compilation compilation,
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            ImmutableArray<ISourceGenerator> generators,
            ImmutableArray<AdditionalText> additionalTexts,
            object? cacheState,
            CancellationToken cancellationToken)
        {
            var (deterministicKey, hashKey) = ((string?, string?))cacheState!;
            CompilationCacheUtilities.OnCompilationSucceeded(_cache, _logger, Arguments, deterministicKey, hashKey, _cacheTelemetry);

            if (_compilationCacheKey is object && _inputCompilation is object)
            {
                _compilationCache?.CacheCompilation(_compilationCacheKey, _inputCompilation.RemoveAllReferences());
            }
        }

        public IReadOnlyList<BuildTelemetryEvent> GetTelemetryEvents()
        {
            var events = new List<BuildTelemetryEvent>(2);
            if (_cacheTelemetry.HasData)
            {
                events.Add(_cacheTelemetry.ToTelemetryEvent(LanguageNames.CSharp));
            }

            if (_incrementalCompilationTelemetry.HasData)
            {
                events.Add(_incrementalCompilationTelemetry.ToTelemetryEvent());
            }

            return events;
        }
    }
}
