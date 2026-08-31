// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.DiaSymReader.Tools;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Emit;

public sealed class IncrementalMethodBodyReuseTests : CSharpTestBase
{
    [Fact]
    public void ReuseUnchangedMethodBody_ProducesIdenticalPeAndPdb()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse(
            """
            public static class Dirty
            {
                public static int Value() => 1;
            }
            """,
            dirtyPath);
        var dirtyTree1 = Parse(
            """
            public static class Dirty
            {
                public static int Value() => 2;
            }
            """,
            dirtyPath);
        var cleanTree = Parse(
            """
            public static class Clean
            {
                public static int Value() => Dirty.Value();
            }
            """,
            cleanPath);

        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(true)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var previousMethod = compilation0.GetTypeByMetadataName("Clean")!.GetMembers("Value").Single();
        var currentMethod = compilation1.GetTypeByMetadataName("Clean")!.GetMembers("Value").Single();
        Assert.NotSame(previousMethod, currentMethod);

        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));
        MethodBodyReuseStatistics? reportedStatistics = null;
        var incremental = Emit(compilation1, reuse, statisticsReceiver: statistics => reportedStatistics = statistics);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        AssertStatistics(incremental.Statistics, total: 4, compiled: 3, attempts: 1, reused: 1, fallbacks: 0);
        Assert.Same(incremental.Statistics, reportedStatistics);
        Assert.Empty(baseline.Diagnostics);
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        Assert.Null(incremental.TestData.Module!.MethodBodyReuse);
        AssertEx.Equal(GetPdbXml(clean), GetPdbXml(incremental));
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void ReuseInstanceSupportsConcurrentTargetCompilations()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", dirtyPath);
        var dirtyTree2 = Parse("public static class Dirty { public static int Value() => 3; }", dirtyPath);
        var cleanTree = Parse("public static class Clean { public static int Value() => Dirty.Value(); }", cleanPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(true)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);
        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var compilation2 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree2);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));

        using var barrier = new Barrier(2);
        var emitTask1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return Emit(compilation1, reuse);
        });
        var emitTask2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return Emit(compilation2, reuse);
        });
        Task.WaitAll(emitTask1, emitTask2);

        var cleanCompilation1 = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var cleanCompilation2 = CreateCompilation([dirtyTree2, cleanTree], assemblyName: "Test", options: options);
        var clean1 = Emit(cleanCompilation1);
        var clean2 = Emit(cleanCompilation2);

        AssertStatistics(emitTask1.Result.Statistics, total: 4, compiled: 3, attempts: 1, reused: 1, fallbacks: 0);
        AssertStatistics(emitTask2.Result.Statistics, total: 4, compiled: 3, attempts: 1, reused: 1, fallbacks: 0);
        Assert.NotSame(emitTask1.Result.Statistics, emitTask2.Result.Statistics);
        AssertBytesEqual(clean1.Pdb, emitTask1.Result.Pdb);
        AssertBytesEqual(clean1.Pe, emitTask1.Result.Pe);
        AssertBytesEqual(clean2.Pdb, emitTask2.Result.Pdb);
        AssertBytesEqual(clean2.Pe, emitTask2.Result.Pe);
    }

    [Fact]
    public void UnsupportedBody_FallsBackToNormalCompilation()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", dirtyPath);
        var cleanTree = Parse(
            """
            public static class Clean
            {
                public static int Value()
                {
                    try
                    {
                        return Dirty.Value();
                    }
                    catch
                    {
                        return 0;
                    }
                }
            }
            """,
            cleanPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        AssertStatistics(incremental.Statistics, total: 4, compiled: 4, attempts: 1, reused: 0, fallbacks: 1);
        Assert.Equal(1, incremental.Statistics.GetBodyFallbackReasonCount(MethodBodyReuseBodyFallbackReason.ExceptionHandling));
        Assert.Empty(baseline.Diagnostics);
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void CompilationWithFields_FallsBackToPreserveUnusedFieldDiagnostics()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", dirtyPath);
        var cleanTree = Parse(
            """
            public static class Clean
            {
                private static int _value;

                public static string Name() => nameof(_value);
                public static int Value() => Dirty.Value();
            }
            """,
            cleanPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        Assert.Equal(2, incremental.Statistics.FallbackBodyCount);
        Assert.Equal(2, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.PreviousDiagnostics));
        Assert.Contains(clean.Diagnostics, diagnostic => diagnostic.Code == (int)ErrorCode.WRN_UnassignedInternalField);
        AssertEx.Equal(
            clean.Diagnostics.Select(diagnostic => diagnostic.ToString()),
            incremental.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void CompilationWithFieldInNamespace_FallsBackToPreserveFieldDiagnostics()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", dirtyPath);
        var cleanTree = Parse(
            """
            namespace N;

            public static class Clean
            {
                private static int _value = 1;

                public static int Value() => _value + Dirty.Value();
            }
            """,
            cleanPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        Assert.Equal(1, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.Fields));
        Assert.Empty(baseline.Diagnostics);
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void DeclarationChangeThatAffectsOverloadResolution_FallsBackToNormalCompilation()
    {
        const string overloadsPath = "Overloads.cs";
        const string callsPath = "Calls.cs";

        var overloadsTree0 = Parse(
            """
            public static class Overloads
            {
                public static int F(long value) => 1;
            }
            """,
            overloadsPath);
        var overloadsTree1 = Parse(
            """
            public static class Overloads
            {
                public static int F(long value) => 1;
                public static int F(int value) => 2;
            }
            """,
            overloadsPath);
        var callsTree = Parse(
            """
            public static class Calls
            {
                public static int M() => Overloads.F(1);
            }
            """,
            callsPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([overloadsTree0, callsTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(overloadsTree0, overloadsTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == callsPath));
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([overloadsTree1, callsTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        Assert.Equal(1, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.Declarations));
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void InstrumentationChange_FallsBackToNormalCompilation()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", dirtyPath);
        var cleanTree = Parse("public static class Clean { public static int Value() => Dirty.Value(); }", cleanPath);
        var instrumentationTree = Parse(
            """
            namespace Microsoft.CodeAnalysis.Runtime
            {
                public static class Instrumentation
                {
                    public static bool[] CreatePayload(
                        System.Guid mvid,
                        int methodToken,
                        int fileIndex,
                        ref bool[] payload,
                        int payloadLength) => payload;

                    public static bool[] CreatePayload(
                        System.Guid mvid,
                        int methodToken,
                        int[] fileIndices,
                        ref bool[] payload,
                        int payloadLength) => payload;
                }
            }
            """,
            "Instrumentation.cs");
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree, instrumentationTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));
        var instrumentedEmitOptions = CreateEmitOptions()
            .WithInstrumentationKinds(ImmutableArray.Create(InstrumentationKind.TestCoverage));
        var incremental = Emit(compilation1, reuse, instrumentedEmitOptions);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree, instrumentationTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation, emitOptions: instrumentedEmitOptions);

        Assert.Equal(1, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.Instrumentation));
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void PreprocessorSymbolChange_FallsBackToNormalCompilation()
    {
        const string path = "Clean.cs";
        const string source = """
            public static class Clean
            {
                public static int Value()
                {
            #if FOO
                    return 2;
            #else
                    return 1;
            #endif
                }
            }
            """;

        var tree0 = Parse(source, path, TestOptions.Regular);
        var tree1 = Parse(source, path, TestOptions.Regular.WithPreprocessorSymbols("FOO"));
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([tree0], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);

        var compilation1 = compilation0.ReplaceSyntaxTree(tree0, tree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.MethodKind == MethodKind.Ordinary);
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([tree1], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        Assert.Equal(1, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.ParseOptions));
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void PdbEmissionChange_FallsBackToNormalCompilation()
    {
        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", path: "");
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => 2; }", path: "");
        var cleanTree = Parse("public static class Clean { public static int Value() => Dirty.Value(); }", path: "");
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(false)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0, emitPdb: false);

        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.ContainingType.Name == "Clean");
        var incremental = Emit(compilation1, reuse);

        var cleanCompilation = CreateCompilation([dirtyTree1, cleanTree], assemblyName: "Test", options: options);
        var clean = Emit(cleanCompilation);

        Assert.False(baseline.TestData.Module!.EmittingPdb);
        Assert.True(incremental.TestData.Module!.EmittingPdb);
        Assert.Equal(1, incremental.Statistics.GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason.DebugInformation));
        Assert.Empty(incremental.Diagnostics);
        Assert.Empty(clean.Diagnostics);
        AssertBytesEqual(clean.Pdb, incremental.Pdb);
        AssertBytesEqual(clean.Pe, incremental.Pe);
    }

    [Fact]
    public void FailedEmit_ReportsFailedStatus()
    {
        const string dirtyPath = "Dirty.cs";
        const string cleanPath = "Clean.cs";

        var dirtyTree0 = Parse("public static class Dirty { public static int Value() => 1; }", dirtyPath);
        var dirtyTree1 = Parse("public static class Dirty { public static int Value() => Missing(); }", dirtyPath);
        var cleanTree = Parse("public static class Clean { public static int Value() => Dirty.Value(); }", cleanPath);
        var options = TestOptions.ReleaseDll
            .WithConcurrentBuild(true)
            .WithDeterministic(true);
        var compilation0 = CreateCompilation([dirtyTree0, cleanTree], assemblyName: "Test", options: options);
        var baseline = Emit(compilation0);
        var compilation1 = compilation0.ReplaceSyntaxTree(dirtyTree0, dirtyTree1);
        var reuse = new CSharpMethodBodyReuse(
            compilation0,
            (PEModuleBuilder)baseline.TestData.Module!,
            baseline.Diagnostics,
            method => method.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == cleanPath));

        var incremental = Emit(compilation1, reuse, expectedSuccess: false);

        Assert.Equal(MethodBodyReuseStatus.Failed, incremental.Statistics.Status);
        Assert.Equal(1, incremental.Statistics.ReuseAttemptCount);
        Assert.Equal(1, incremental.Statistics.ReusedBodyCount);
        Assert.Equal(0, incremental.Statistics.FallbackBodyCount);
    }

    private static SyntaxTree Parse(string source, string path, CSharpParseOptions? options = null)
        => CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), options, path);

    private static EmitOptions CreateEmitOptions()
        => new(
            debugInformationFormat: DebugInformationFormat.PortablePdb,
            pdbFilePath: "test.pdb");

    private static string GetPdbXml(EmitOutput output)
        => PdbToXmlConverter.ToXml(
            new MemoryStream(output.Pdb.ToArray()),
            new MemoryStream(output.Pe.ToArray()),
            PdbToXmlOptions.ResolveTokens | PdbToXmlOptions.ThrowOnError | PdbToXmlOptions.IncludeTokens);

    private static void AssertBytesEqual(ImmutableArray<byte> expected, ImmutableArray<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        var mismatches = new StringBuilder();
        var mismatchCount = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                mismatchCount++;
                if (mismatchCount <= 20)
                {
                    mismatches.Append($"{i}:0x{expected[i]:X2}/0x{actual[i]:X2} ");
                }
            }
        }

        Assert.True(mismatchCount == 0, $"{mismatchCount} mismatches: {mismatches}");
    }

    private static void AssertStatistics(
        MethodBodyReuseStatistics statistics,
        int total,
        int compiled,
        int attempts,
        int reused,
        int fallbacks)
    {
        Assert.Equal(MethodBodyReuseStatus.Succeeded, statistics.Status);
        Assert.Equal(total, statistics.TotalBodyCount);
        Assert.Equal(compiled, statistics.CompiledBodyCount);
        Assert.Equal(attempts, statistics.ReuseAttemptCount);
        Assert.Equal(reused, statistics.ReusedBodyCount);
        Assert.Equal(fallbacks, statistics.FallbackBodyCount);
        Assert.Equal(total, compiled + reused);
        Assert.Equal(attempts, reused + fallbacks);
    }

    private static EmitOutput Emit(
        CSharpCompilation compilation,
        IMethodBodyReuse? methodBodyReuse = null,
        EmitOptions? emitOptions = null,
        bool emitPdb = true,
        bool expectedSuccess = true,
        Action<MethodBodyReuseStatistics>? statisticsReceiver = null)
    {
        using var peStream = new MemoryStream();
        using var pdbStream = emitPdb ? new MemoryStream() : null;
        var testData = new CompilationTestData();
        var result = compilation.Emit(
            peStream,
            metadataPEStream: null,
            pdbStream,
            xmlDocumentationStream: null,
            win32Resources: null,
            manifestResources: null,
            options: emitOptions ?? CreateEmitOptions(),
            debugEntryPoint: null,
            sourceLinkStream: null,
            embeddedTexts: null,
            rebuildData: null,
            testData,
            cancellationToken: CancellationToken.None,
            methodBodyReuse,
            statisticsReceiver);

        Assert.Equal(expectedSuccess, result.Success);
        return new(
            ImmutableArray.CreateRange(peStream.ToArray()),
            pdbStream is null ? ImmutableArray<byte>.Empty : ImmutableArray.CreateRange(pdbStream.ToArray()),
            testData,
            result.Diagnostics);
    }

    private readonly record struct EmitOutput(
        ImmutableArray<byte> Pe,
        ImmutableArray<byte> Pdb,
        CompilationTestData TestData,
        ImmutableArray<Diagnostic> Diagnostics)
    {
        internal MethodBodyReuseStatistics Statistics
            => TestData.Module!.MethodBodyReuseStatistics!;
    }
}
