// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET && BUILDXL_COMPILER_CACHE
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis.CommandLine;
using Microsoft.CodeAnalysis.CSharp;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CompilerServer.UnitTests;

public sealed class BuildXLCompilationCacheTests(ITestOutputHelper output) : TestBase
{
    private static readonly DateTime s_timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private readonly ICompilerServerLogger _logger = new XunitCompilerServerLogger(output);

    public sealed class SupportedPlatform : ExecutionCondition
    {
        public override bool ShouldSkip => !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64;
        public override string SkipReason => "The BuildXL compiler-cache prototype requires Windows x64.";
    }

    [ConditionalTheory(typeof(SupportedPlatform)), CombinatorialData]
    public void RoundTripAfterReopening_PreservesRequestedOutputs(bool pdb, bool referenceAssembly, bool xml)
    {
        var root = Temp.CreateDirectory().Path;
        var source = CreateOutputs(Temp.CreateDirectory().Path, pdb, referenceAssembly, xml);
        WriteOutputs(source);
        var first = new BuildXLCompilationCache(root);
        Assert.Equal(CompilationCacheStoreResult.Stored, first.TryStoreResult("test.dll", "key", source, "key", _logger, CancellationToken.None));

        var destination = CreateOutputs(Temp.CreateDirectory().Path, pdb, referenceAssembly, xml);
        var second = new BuildXLCompilationCache(root);
        Assert.True(second.TryRestoreCachedResult("test.dll", "key", destination, _logger, s_timestamp, CancellationToken.None));
        AssertOutput(destination.AssemblyPath, 1);
        if (pdb)
            AssertOutput(destination.PdbPath!, 2);
        if (referenceAssembly)
            AssertOutput(destination.RefAssemblyPath!, 3);
        if (xml)
            AssertOutput(destination.XmlDocPath!, 4);
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void MissingEntry_DoesNotOverwriteOutputs()
    {
        var cache = new BuildXLCompilationCache(Temp.CreateDirectory().Path);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path);
        File.WriteAllBytes(outputs.AssemblyPath, [99]);
        Assert.False(cache.TryRestoreCachedResult("test.dll", "missing", outputs, _logger, s_timestamp, CancellationToken.None));
        Assert.Equal([99], File.ReadAllBytes(outputs.AssemblyPath));
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void DifferentKeyOrOutputContract_DoesNotReuseEntry()
    {
        var cache = new BuildXLCompilationCache(Temp.CreateDirectory().Path);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path);
        WriteOutputs(outputs);
        Assert.Equal(CompilationCacheStoreResult.Stored, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        var destination = CreateOutputs(Temp.CreateDirectory().Path);
        Assert.False(cache.TryRestoreCachedResult("test.dll", "changed", destination, _logger, s_timestamp, CancellationToken.None));
        Assert.False(cache.TryRestoreCachedResult("other.dll", "key", destination, _logger, s_timestamp, CancellationToken.None));
        Assert.False(cache.TryRestoreCachedResult("test.dll", "key", destination with { PdbPath = destination.AssemblyPath + ".pdb" }, _logger, s_timestamp, CancellationToken.None));
        Assert.False(File.Exists(destination.AssemblyPath));
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void MissingRequiredOutput_DoesNotPublishEntry()
    {
        var cache = new BuildXLCompilationCache(Temp.CreateDirectory().Path);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path, pdb: true);
        File.WriteAllBytes(outputs.AssemblyPath, [1]);
        Assert.Equal(CompilationCacheStoreResult.Failed, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        var destination = CreateOutputs(Temp.CreateDirectory().Path, pdb: true);
        Assert.False(cache.TryRestoreCachedResult("test.dll", "key", destination, _logger, s_timestamp, CancellationToken.None));
        Assert.False(File.Exists(destination.AssemblyPath));
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void MissingContent_DoesNotPartiallyRestoreOutputs()
    {
        var root = Temp.CreateDirectory().Path;
        var cache = new BuildXLCompilationCache(root);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path, pdb: true);
        WriteOutputs(outputs);
        Assert.Equal(CompilationCacheStoreResult.Stored, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        var blobs = Directory.GetFiles(root, "*.blob", SearchOption.AllDirectories);
        Assert.NotEmpty(blobs);
        foreach (var blob in blobs)
            File.Delete(blob);

        var destination = CreateOutputs(Temp.CreateDirectory().Path, pdb: true);
        File.WriteAllBytes(destination.AssemblyPath, [99]);
        Assert.False(cache.TryRestoreCachedResult("test.dll", "key", destination, _logger, s_timestamp, CancellationToken.None));
        Assert.Equal([99], File.ReadAllBytes(destination.AssemblyPath));
        Assert.False(File.Exists(destination.PdbPath));
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void ConflictingOutputs_DoNotReplacePublishedEntry()
    {
        var cache = new BuildXLCompilationCache(Temp.CreateDirectory().Path);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path);
        WriteOutputs(outputs);
        Assert.Equal(CompilationCacheStoreResult.Stored, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        Assert.Equal(CompilationCacheStoreResult.SkippedExists, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        File.WriteAllBytes(outputs.AssemblyPath, [99]);
        Assert.Equal(CompilationCacheStoreResult.Failed, cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, CancellationToken.None));
        var destination = CreateOutputs(Temp.CreateDirectory().Path);
        Assert.True(cache.TryRestoreCachedResult("test.dll", "key", destination, _logger, s_timestamp, CancellationToken.None));
        AssertOutput(destination.AssemblyPath, 1);
    }

    [ConditionalTheory(typeof(SupportedPlatform))]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_DoesNotCreateCache(bool store)
    {
        var root = Path.Combine(Temp.CreateDirectory().Path, "cache");
        var cache = new BuildXLCompilationCache(root);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (store)
            Assert.Throws<OperationCanceledException>(() => cache.TryStoreResult("test.dll", "key", outputs, "key", _logger, cancellation.Token));
        else
            Assert.Throws<OperationCanceledException>(() => cache.TryRestoreCachedResult("test.dll", "key", outputs, _logger, s_timestamp, cancellation.Token));
        Assert.False(Directory.Exists(root));
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void UnavailableCache_LogsFailure()
    {
        var root = Path.Combine(Temp.CreateDirectory().Path, "file");
        File.WriteAllBytes(root, []);
        var cache = new BuildXLCompilationCache(root);
        var messages = new List<string>();
        var logger = new CollectingLogger(messages);
        var outputs = CreateOutputs(Temp.CreateDirectory().Path);
        Assert.False(cache.TryRestoreCachedResult("test.dll", "key", outputs, logger, s_timestamp, CancellationToken.None));
        Assert.Equal(CompilationCacheStoreResult.Failed, cache.TryStoreResult("test.dll", "key", outputs, "key", logger, CancellationToken.None));
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Contains("BuildXL cache operation failed:", message, StringComparison.Ordinal));
    }

    [ConditionalTheory(typeof(SupportedPlatform))]
    [InlineData("/features:use-buildxl-cache", "/deterministic+")]
    [InlineData("/features:use-buildxl-cache=relative", "/deterministic+")]
    [InlineData("/features:use-buildxl-cache=C:\\cache", "/deterministic-")]
    public void Factory_InvalidOptIn_DisablesCacheAndLogs(string feature, string deterministic)
    {
        var arguments = CSharpCommandLineParser.Default.Parse([feature, deterministic, "test.cs"], Directory.GetCurrentDirectory(), sdkDirectory: null);
        var messages = new List<string>();
        Assert.Null(CompilationCacheFactory.TryCreate(arguments, new CollectingLogger(messages)));
        Assert.Contains("BuildXL compilation cache disabled:", Assert.Single(messages), StringComparison.Ordinal);
    }

    [ConditionalFact(typeof(SupportedPlatform))]
    public void Factory_BuildXLFlagTakesPrecedenceOverDirectoryCache()
    {
        var root = Temp.CreateDirectory().Path;
        var arguments = CSharpCommandLineParser.Default.Parse(
            ["/features:use-global-cache", $"/features:use-buildxl-cache={root}", "/deterministic+", "test.cs"],
            Directory.GetCurrentDirectory(), sdkDirectory: null);
        Assert.IsType<BuildXLCompilationCache>(CompilationCacheFactory.TryCreate(arguments, _logger));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    private static CompilationOutputFiles CreateOutputs(string directory, bool pdb = false, bool referenceAssembly = false, bool xml = false)
        => new()
        {
            AssemblyPath = Path.Combine(directory, "test.dll"),
            PdbPath = pdb ? Path.Combine(directory, "test.pdb") : null,
            RefAssemblyPath = referenceAssembly ? Path.Combine(directory, "ref.dll") : null,
            XmlDocPath = xml ? Path.Combine(directory, "test.xml") : null,
        };

    private static void WriteOutputs(CompilationOutputFiles outputs)
    {
        File.WriteAllBytes(outputs.AssemblyPath, [1]);
        if (outputs.PdbPath is { } pdb)
            File.WriteAllBytes(pdb, [2]);
        if (outputs.RefAssemblyPath is { } reference)
            File.WriteAllBytes(reference, [3]);
        if (outputs.XmlDocPath is { } xml)
            File.WriteAllBytes(xml, [4]);
    }

    private static void AssertOutput(string path, byte expected)
    {
        Assert.Equal([expected], File.ReadAllBytes(path));
        Assert.Equal(s_timestamp, File.GetLastWriteTimeUtc(path));
    }

    private sealed class CollectingLogger(List<string> messages) : ICompilerServerLogger
    {
        private readonly List<string> _messages = messages;

        public bool IsLogging => true;
        public void Log(string message) => _messages.Add(message);
    }
}
#endif
