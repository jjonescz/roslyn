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
using Microsoft.CodeAnalysis.Emit;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    internal sealed class CSharpCompilerServer : CSharpCompiler, ICompilerServerTelemetryProvider
    {
        private readonly Func<string, MetadataReferenceProperties, PortableExecutableReference> _metadataProvider;
        private readonly CompilationCache? _cache;
        private readonly CompilationCacheTelemetry _cacheTelemetry = new CompilationCacheTelemetry();
        private readonly IncrementalCompilationTelemetry _incrementalCompilationTelemetry;
        private readonly ICompilerServerLogger _logger;

        internal CSharpCompilerServer(Func<string, MetadataReferenceProperties, PortableExecutableReference> metadataProvider, string[] args, BuildPaths buildPaths, string? libDirectory, IAnalyzerAssemblyLoader analyzerLoader, GeneratorDriverCache driverCache, ICompilerServerLogger? logger = null)
            : this(metadataProvider, Path.Combine(buildPaths.ClientDirectory, ResponseFileName), args, buildPaths, libDirectory, analyzerLoader, driverCache, logger)
        {
        }

        internal CSharpCompilerServer(Func<string, MetadataReferenceProperties, PortableExecutableReference> metadataProvider, string? responseFile, string[] args, BuildPaths buildPaths, string? libDirectory, IAnalyzerAssemblyLoader analyzerLoader, GeneratorDriverCache driverCache, ICompilerServerLogger? logger = null)
            : base(CSharpCommandLineParser.Default, responseFile, args, buildPaths, libDirectory, analyzerLoader, driverCache)
        {
            _metadataProvider = metadataProvider;
            _logger = logger ?? EmptyCompilerServerLogger.Instance;
            _cache = CompilationCache.TryCreate(Arguments, _logger);
            _incrementalCompilationTelemetry = new IncrementalCompilationTelemetry(_logger);
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
        }

        protected override void OnCompilationCompleted(bool succeeded)
        {
            _cacheTelemetry.StopCompileTimer(succeeded);
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

        /// <summary>
        /// Records the result produced by the request's method-body reuse emit. The warm compilation
        /// owner calls this seam when it supplies method-body reuse state to emit.
        /// </summary>
        internal void RecordMethodBodyReuseStatistics(MethodBodyReuseStatistics statistics)
            => _incrementalCompilationTelemetry.RecordMethodBodyReuse(statistics);
    }
}
