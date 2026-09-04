// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CommandLine;

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

    internal enum IncrementalCompilationStatus
    {
        None,
        Succeeded,
        Failed,
    }

    /// <summary>
    /// Accumulates compilation-reuse statistics and timings for a single C# compiler-server request.
    /// </summary>
    internal sealed class IncrementalCompilationTelemetry
    {
        internal const string EventName = "roslyn/incrementalcompilation";

        private readonly ICompilerServerLogger _logger;
        private readonly Stopwatch _compileAndEmitStopwatch = new();
        private bool _summaryLogged;

        internal IncrementalCompilationStatus Status { get; private set; }
        internal bool ReusedCompilation { get; private set; }
        internal int TotalSyntaxTreeCount { get; private set; }
        internal int ReusedSyntaxTreeCount { get; private set; }
        internal long CompilationCreationMilliseconds { get; private set; }
        internal long CompilationUpdateMilliseconds { get; private set; }
        internal long CompileMethodsMilliseconds { get; private set; }
        internal long? SerializationMilliseconds { get; private set; }
        internal long CompileAndEmitMilliseconds { get; private set; }
        internal bool OutputCacheHit { get; private set; }

        internal IncrementalCompilationTelemetry(ICompilerServerLogger logger)
        {
            _logger = logger;
        }

        internal bool HasData => Status != IncrementalCompilationStatus.None;

        internal void RecordCompilationReuse(
            bool reusedCompilation,
            int reusedSyntaxTreeCount,
            int totalSyntaxTreeCount,
            long updateMilliseconds)
        {
            ReusedCompilation = reusedCompilation;
            ReusedSyntaxTreeCount = reusedSyntaxTreeCount;
            TotalSyntaxTreeCount = totalSyntaxTreeCount;
            CompilationUpdateMilliseconds = updateMilliseconds;
        }

        internal void RecordCompilationCreation(long elapsedMilliseconds)
            => CompilationCreationMilliseconds = elapsedMilliseconds;

        internal void StartCompileAndEmit()
        {
            Debug.Assert(!_compileAndEmitStopwatch.IsRunning);
            _compileAndEmitStopwatch.Restart();
        }

        internal void RecordCompileMethods(long elapsedMilliseconds)
            => CompileMethodsMilliseconds = elapsedMilliseconds;

        internal void RecordSerialization(long elapsedMilliseconds)
            => SerializationMilliseconds = elapsedMilliseconds;

        internal void Complete(bool succeeded)
        {
            if (_compileAndEmitStopwatch.IsRunning)
            {
                _compileAndEmitStopwatch.Stop();
                CompileAndEmitMilliseconds = _compileAndEmitStopwatch.ElapsedMilliseconds;
            }

            Status = succeeded
                ? IncrementalCompilationStatus.Succeeded
                : IncrementalCompilationStatus.Failed;
        }

        internal void CompleteFromOutputCache()
        {
            Debug.Assert(!_compileAndEmitStopwatch.IsRunning);
            OutputCacheHit = true;
            Status = IncrementalCompilationStatus.Succeeded;
        }

        internal BuildTelemetryEvent ToTelemetryEvent()
        {
            Debug.Assert(HasData);
            var properties = CreateProperties();
            if (!_summaryLogged)
            {
                var builder = new StringBuilder("Incremental compilation");
                foreach (var property in properties)
                {
                    builder.Append(' ');
                    builder.Append(property.Key);
                    builder.Append('=');
                    builder.Append(property.Value);
                }

                _logger.Log(builder.ToString());
                _summaryLogged = true;
            }

            return new BuildTelemetryEvent(EventName, properties);
        }

        private Dictionary<string, string> CreateProperties()
        {
            var properties = new Dictionary<string, string>(12)
            {
                ["strategy"] = "compilationreuse",
                ["cachekind"] = "memory",
                ["status"] = Status == IncrementalCompilationStatus.Succeeded ? "succeeded" : "failed",
                ["cachestatus"] = ReusedCompilation ? "hit" : "miss",
                ["outputcachehit"] = OutputCacheHit ? "true" : "false",
                ["totalsyntaxtreecount"] = ToInvariantString(TotalSyntaxTreeCount),
                ["reusedsyntaxtreecount"] = ToInvariantString(ReusedSyntaxTreeCount),
                ["compilationcreatems"] = ToInvariantString(CompilationCreationMilliseconds),
                ["compilationupdatems"] = ToInvariantString(CompilationUpdateMilliseconds),
                ["compilemethodsms"] = ToInvariantString(CompileMethodsMilliseconds),
                ["compileandemitms"] = ToInvariantString(CompileAndEmitMilliseconds),
            };

            if (SerializationMilliseconds is { } serializationMilliseconds)
            {
                properties["serializems"] = ToInvariantString(serializationMilliseconds);
            }

            return properties;
        }

        private static string ToInvariantString(long value)
            => value.ToString(CultureInfo.InvariantCulture);
    }
}
