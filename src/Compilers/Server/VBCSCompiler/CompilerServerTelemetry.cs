// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CommandLine;
using Microsoft.CodeAnalysis.Emit;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    /// <summary>
    /// Implemented by compiler hosts that can produce telemetry for a build request. The request
    /// handler collects these events after a compilation completes and returns them to the client in
    /// the <see cref="CompletedBuildResponse"/>. The build task then forwards each event to the host
    /// via <c>IBuildEngine5.LogTelemetry</c>.
    /// </summary>
    internal interface ICompilerServerTelemetryProvider
    {
        /// <summary>
        /// Returns the telemetry events collected for the current request. Returns an empty list
        /// when there is nothing to report.
        /// </summary>
        IReadOnlyList<BuildTelemetryEvent> GetTelemetryEvents();
    }

    /// <summary>
    /// Outcome of the compilation cache lookup for a single request.
    /// </summary>
    internal enum CompilationCacheStatus
    {
        /// <summary>The cache did not run for this request (disabled or not applicable).</summary>
        None,

        /// <summary>A cached result was found and restored.</summary>
        Hit,

        /// <summary>No cached result was found; a normal compilation was performed.</summary>
        Miss,
    }

    /// <summary>
    /// Outcome of an attempt to store a compilation result in the cache.
    /// </summary>
    internal enum CompilationCacheStoreResult
    {
        /// <summary>No store was attempted (for example, on a cache hit).</summary>
        None,

        /// <summary>The result was stored successfully.</summary>
        Stored,

        /// <summary>Another writer was already populating the entry.</summary>
        SkippedRace,

        /// <summary>The entry already existed when the store was attempted.</summary>
        SkippedExists,

        /// <summary>The store attempt failed.</summary>
        Failed,
    }

    /// <summary>
    /// Outcome of compilation and emit after a cache miss.
    /// </summary>
    internal enum CompilationCacheCompileResult
    {
        /// <summary>No compilation ran (for example, on a cache hit).</summary>
        None,

        /// <summary>Compilation and emit completed successfully.</summary>
        Succeeded,

        /// <summary>Compilation or emit failed.</summary>
        Failed,
    }

    /// <summary>
    /// Accumulates compilation-cache statistics for a single request and converts them into a
    /// generic <see cref="BuildTelemetryEvent"/>.
    /// </summary>
    internal sealed class CompilationCacheTelemetry
    {
        /// <summary>
        /// The telemetry event name reported by the task. Host telemetry pipelines prefix this
        /// (Visual Studio: <c>vs/</c>, dotnet CLI: <c>dotnet/cli/msbuild/</c>).
        /// </summary>
        internal const string EventName = "roslyn/compilercache";

        public CompilationCacheStatus Status { get; set; }
        public CompilationCacheStoreResult StoreResult { get; set; }
        public CompilationCacheCompileResult CompileResult { get; set; }
        public long KeyComputeMilliseconds { get; set; }
        public long RestoreMilliseconds { get; set; }
        public long? StoreMilliseconds { get; set; }

        /// <summary>
        /// Wall-clock time spent compiling and emitting on a cache miss, or <see langword="null"/>
        /// when no compilation ran (a cache hit).
        /// </summary>
        public long? CompileMilliseconds { get; set; }

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private bool _compileTimerRunning;

        public void StartKeyComputeTimer() => StartTimer();
        public void StopKeyComputeTimer() => KeyComputeMilliseconds = StopTimer();

        public void StartRestoreTimer() => StartTimer();
        public void StopRestoreTimer() => RestoreMilliseconds = StopTimer();

        public void StartCompileTimer()
        {
            StartTimer();
            _compileTimerRunning = true;
        }

        public void StopCompileTimer(bool succeeded)
        {
            if (_compileTimerRunning)
            {
                CompileMilliseconds = StopTimer();
                CompileResult = succeeded
                    ? CompilationCacheCompileResult.Succeeded
                    : CompilationCacheCompileResult.Failed;
                _compileTimerRunning = false;
            }
        }

        public void StartStoreTimer() => StartTimer();
        public void StopStoreTimer() => StoreMilliseconds = StopTimer();

        private void StartTimer([CallerMemberName] string? callerName = null)
        {
            Debug.Assert(!_stopwatch.IsRunning, $"A telemetry timer is already running when {callerName} was called.");
            _stopwatch.Restart();
        }

        private long StopTimer()
        {
            _stopwatch.Stop();
            return _stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// True when the cache actually ran for this request and there is something to report.
        /// </summary>
        public bool HasData => Status != CompilationCacheStatus.None;

        public BuildTelemetryEvent ToTelemetryEvent(string language)
        {
            var properties = new Dictionary<string, string>(8)
            {
                ["cachestatus"] = Status switch
                {
                    CompilationCacheStatus.Hit => "hit",
                    CompilationCacheStatus.Miss => "miss",
                    _ => "none",
                },
                ["storeresult"] = StoreResult switch
                {
                    CompilationCacheStoreResult.Stored => "stored",
                    CompilationCacheStoreResult.SkippedRace => "skippedrace",
                    CompilationCacheStoreResult.SkippedExists => "skippedexists",
                    CompilationCacheStoreResult.Failed => "failed",
                    _ => "none",
                },
                ["language"] = language,
                ["keycomputems"] = KeyComputeMilliseconds.ToString(CultureInfo.InvariantCulture),
                ["restorems"] = RestoreMilliseconds.ToString(CultureInfo.InvariantCulture),
            };

            if (CompileResult != CompilationCacheCompileResult.None)
            {
                properties["compileresult"] = CompileResult == CompilationCacheCompileResult.Succeeded
                    ? "succeeded"
                    : "failed";
            }

            if (StoreMilliseconds is { } storeMs)
            {
                properties["storems"] = storeMs.ToString(CultureInfo.InvariantCulture);
            }

            if (CompileMilliseconds is { } compileMs)
            {
                properties["compilems"] = compileMs.ToString(CultureInfo.InvariantCulture);
            }

            return new BuildTelemetryEvent(EventName, properties);
        }
    }

    /// <summary>
    /// Converts compiler-domain method-body reuse statistics into request telemetry and logging.
    /// </summary>
    internal sealed class IncrementalCompilationTelemetry
    {
        internal const string EventName = "roslyn/incrementalcompilation";

        private readonly ICompilerServerLogger _logger;
        private MethodBodyReuseStatistics? _statistics;
        private bool _summaryLogged;

        internal IncrementalCompilationTelemetry(ICompilerServerLogger logger)
        {
            _logger = logger;
        }

        internal bool HasData => _statistics is object;

        internal void RecordMethodBodyReuse(MethodBodyReuseStatistics statistics)
        {
            Debug.Assert(_statistics is null);

            _statistics = statistics;
        }

        internal BuildTelemetryEvent ToTelemetryEvent()
        {
            Debug.Assert(_statistics is object);
            var statistics = _statistics!;
            if (!_summaryLogged)
            {
                _logger.Log(CreateSummary(statistics));
                _summaryLogged = true;
            }

            var properties = CreateProperties(statistics);
            return new BuildTelemetryEvent(EventName, properties);
        }

        private static Dictionary<string, string> CreateProperties(MethodBodyReuseStatistics statistics)
        {
            var properties = new Dictionary<string, string>
            {
                ["strategy"] = "methodbodyreuse",
                ["cachekind"] = "memory",
                ["status"] = GetStatus(statistics.Status),
                ["totalbodycount"] = ToInvariantString(statistics.TotalBodyCount),
                ["compiledbodycount"] = ToInvariantString(statistics.CompiledBodyCount),
                ["reuseattemptcount"] = ToInvariantString(statistics.ReuseAttemptCount),
                ["reusedbodycount"] = ToInvariantString(statistics.ReusedBodyCount),
                ["fallbackbodycount"] = ToInvariantString(statistics.FallbackBodyCount),
            };

            AddNonzeroReasonCounts(properties, statistics);
            return properties;
        }

        private static void AddNonzeroReasonCounts(
            Dictionary<string, string> properties,
            MethodBodyReuseStatistics statistics)
        {
            foreach (MethodBodyReuseGlobalFallbackReason reason in System.Enum.GetValues(typeof(MethodBodyReuseGlobalFallbackReason)))
            {
                var count = statistics.GetGlobalFallbackReasonCount(reason);
                if (count != 0)
                {
                    properties["globalreasoncount." + GetReasonName(reason)] = ToInvariantString(count);
                }
            }

            foreach (MethodBodyReuseBodyFallbackReason reason in System.Enum.GetValues(typeof(MethodBodyReuseBodyFallbackReason)))
            {
                var count = statistics.GetBodyFallbackReasonCount(reason);
                if (count != 0)
                {
                    properties["bodyreasoncount." + GetReasonName(reason)] = ToInvariantString(count);
                }
            }
        }

        private static string CreateSummary(MethodBodyReuseStatistics statistics)
        {
            var properties = CreateProperties(statistics);
            var builder = new StringBuilder("Incremental compilation");
            foreach (var property in properties)
            {
                builder.Append(' ');
                builder.Append(property.Key);
                builder.Append('=');
                builder.Append(property.Value);
            }

            return builder.ToString();
        }

        private static string GetStatus(MethodBodyReuseStatus status)
            => status switch
            {
                MethodBodyReuseStatus.Succeeded => "succeeded",
                MethodBodyReuseStatus.Failed => "failed",
                _ => throw ExceptionUtilities.UnexpectedValue(status),
            };

        private static string GetReasonName(MethodBodyReuseGlobalFallbackReason reason)
            => reason switch
            {
                MethodBodyReuseGlobalFallbackReason.PreviousDiagnostics => "previousdiagnostics",
                MethodBodyReuseGlobalFallbackReason.CompilationOptions => "compilationoptions",
                MethodBodyReuseGlobalFallbackReason.AssemblyIdentity => "assemblyidentity",
                MethodBodyReuseGlobalFallbackReason.References => "references",
                MethodBodyReuseGlobalFallbackReason.DebugInformation => "debuginformation",
                MethodBodyReuseGlobalFallbackReason.Instrumentation => "instrumentation",
                MethodBodyReuseGlobalFallbackReason.Declarations => "declarations",
                MethodBodyReuseGlobalFallbackReason.ParseOptions => "parseoptions",
                MethodBodyReuseGlobalFallbackReason.Fields => "fields",
                _ => throw ExceptionUtilities.UnexpectedValue(reason),
            };

        private static string GetReasonName(MethodBodyReuseBodyFallbackReason reason)
            => reason switch
            {
                MethodBodyReuseBodyFallbackReason.PreviousSymbolUnavailable => "previoussymbolunavailable",
                MethodBodyReuseBodyFallbackReason.PreviousBodyUnavailable => "previousbodyunavailable",
                MethodBodyReuseBodyFallbackReason.SourceChanged => "sourcechanged",
                MethodBodyReuseBodyFallbackReason.Locals => "locals",
                MethodBodyReuseBodyFallbackReason.ExceptionHandling => "exceptionhandling",
                MethodBodyReuseBodyFallbackReason.Imports => "imports",
                MethodBodyReuseBodyFallbackReason.StateMachine => "statemachine",
                MethodBodyReuseBodyFallbackReason.SynthesizedDebugInformation => "synthesizeddebuginformation",
                MethodBodyReuseBodyFallbackReason.Dynamic => "dynamic",
                MethodBodyReuseBodyFallbackReason.StackAlloc => "stackalloc",
                MethodBodyReuseBodyFallbackReason.CodeCoverage => "codecoverage",
                MethodBodyReuseBodyFallbackReason.InlineSignature => "inlinesignature",
                MethodBodyReuseBodyFallbackReason.ReferenceMapping => "referencemapping",
                MethodBodyReuseBodyFallbackReason.SequencePointDocument => "sequencepointdocument",
                MethodBodyReuseBodyFallbackReason.StringToken => "stringtoken",
                MethodBodyReuseBodyFallbackReason.UnsupportedInstruction => "unsupportedinstruction",
                MethodBodyReuseBodyFallbackReason.FieldUsageMapping => "fieldusagemapping",
                _ => throw ExceptionUtilities.UnexpectedValue(reason),
            };

        private static string ToInvariantString(int value)
            => value.ToString(CultureInfo.InvariantCulture);
    }
}
