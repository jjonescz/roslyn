// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.CodeAnalysis.Emit;

internal enum MethodBodyReuseStatus
{
    Succeeded = 1,
    Failed = 2,
}

internal enum MethodBodyReuseGlobalFallbackReason
{
    PreviousDiagnostics = 0,
    CompilationOptions = 1,
    AssemblyIdentity = 2,
    References = 3,
    DebugInformation = 4,
    Instrumentation = 5,
    Declarations = 6,
    ParseOptions = 7,
    Fields = 8,
}

internal enum MethodBodyReuseBodyFallbackReason
{
    PreviousSymbolUnavailable = 0,
    PreviousBodyUnavailable = 1,
    SourceChanged = 2,
    Locals = 3,
    ExceptionHandling = 4,
    Imports = 5,
    StateMachine = 6,
    SynthesizedDebugInformation = 7,
    Dynamic = 8,
    StackAlloc = 9,
    CodeCoverage = 10,
    InlineSignature = 11,
    ReferenceMapping = 12,
    SequencePointDocument = 13,
    StringToken = 14,
    UnsupportedInstruction = 15,
}

/// <summary>
/// Immutable statistics for one method-body reuse emit.
/// </summary>
/// <remarks>
/// <see cref="TotalBodyCount"/> counts bodies installed in the module, including source and
/// synthesized bodies. <see cref="CompiledBodyCount"/> counts those produced by normal compilation,
/// while <see cref="ReusedBodyCount"/> counts those copied from the baseline. A reuse attempt is made
/// only for an ordinary source method selected by the reuse predicate. Each unsuccessful attempt is
/// one fallback and has exactly one bounded global or per-body reason.
/// </remarks>
internal sealed class MethodBodyReuseStatistics
{
    internal MethodBodyReuseStatus Status { get; }
    internal int TotalBodyCount { get; }
    internal int CompiledBodyCount { get; }
    internal int ReuseAttemptCount { get; }
    internal int ReusedBodyCount { get; }
    internal int FallbackBodyCount { get; }
    internal ImmutableArray<int> GlobalFallbackReasonCounts { get; }
    internal ImmutableArray<int> BodyFallbackReasonCounts { get; }

    internal MethodBodyReuseStatistics(
        MethodBodyReuseStatus status,
        int compiledBodyCount,
        int reuseAttemptCount,
        int reusedBodyCount,
        int fallbackBodyCount,
        ImmutableArray<int> globalFallbackReasonCounts,
        ImmutableArray<int> bodyFallbackReasonCounts)
    {
        Debug.Assert(compiledBodyCount >= 0);
        Debug.Assert(reuseAttemptCount >= 0);
        Debug.Assert(reusedBodyCount >= 0);
        Debug.Assert(fallbackBodyCount >= 0);
        Debug.Assert(reuseAttemptCount == reusedBodyCount + fallbackBodyCount);
        Debug.Assert(fallbackBodyCount == Sum(globalFallbackReasonCounts) + Sum(bodyFallbackReasonCounts));

        Status = status;
        CompiledBodyCount = compiledBodyCount;
        ReuseAttemptCount = reuseAttemptCount;
        ReusedBodyCount = reusedBodyCount;
        FallbackBodyCount = fallbackBodyCount;
        TotalBodyCount = compiledBodyCount + reusedBodyCount;
        GlobalFallbackReasonCounts = globalFallbackReasonCounts;
        BodyFallbackReasonCounts = bodyFallbackReasonCounts;
    }

    internal int GetGlobalFallbackReasonCount(MethodBodyReuseGlobalFallbackReason reason)
        => GlobalFallbackReasonCounts[(int)reason];

    internal int GetBodyFallbackReasonCount(MethodBodyReuseBodyFallbackReason reason)
        => BodyFallbackReasonCounts[(int)reason];

    private static int Sum(ImmutableArray<int> counts)
    {
        var sum = 0;
        foreach (var count in counts)
        {
            sum += count;
        }

        return sum;
    }
}

internal sealed class MethodBodyReuseStatisticsCollector
{
    private readonly int[] _globalFallbackReasonCounts = new int[(int)MethodBodyReuseGlobalFallbackReason.Fields + 1];
    private readonly int[] _bodyFallbackReasonCounts = new int[(int)MethodBodyReuseBodyFallbackReason.UnsupportedInstruction + 1];
    private int _compiledBodyCount;
    private int _reuseAttemptCount;
    private int _reusedBodyCount;
    private int _fallbackBodyCount;
    private int _completed;

    internal void RecordCompiledBody()
        => Interlocked.Increment(ref _compiledBodyCount);

    internal void RecordReusedBody()
        => Interlocked.Increment(ref _reusedBodyCount);

    internal void RecordReuseAttempt()
        => Interlocked.Increment(ref _reuseAttemptCount);

    internal void RecordFallback(MethodBodyReuseGlobalFallbackReason reason)
    {
        Interlocked.Increment(ref _fallbackBodyCount);
        Interlocked.Increment(ref _globalFallbackReasonCounts[(int)reason]);
    }

    internal void RecordFallback(MethodBodyReuseBodyFallbackReason reason)
    {
        Interlocked.Increment(ref _fallbackBodyCount);
        Interlocked.Increment(ref _bodyFallbackReasonCounts[(int)reason]);
    }

    internal MethodBodyReuseStatistics Complete(bool succeeded)
    {
        Debug.Assert(Interlocked.Exchange(ref _completed, 1) == 0);

        var globalFallbackReasonCounts = ImmutableArray.CreateBuilder<int>(_globalFallbackReasonCounts.Length);
        for (var i = 0; i < _globalFallbackReasonCounts.Length; i++)
        {
            globalFallbackReasonCounts.Add(Volatile.Read(ref _globalFallbackReasonCounts[i]));
        }

        var bodyFallbackReasonCounts = ImmutableArray.CreateBuilder<int>(_bodyFallbackReasonCounts.Length);
        for (var i = 0; i < _bodyFallbackReasonCounts.Length; i++)
        {
            bodyFallbackReasonCounts.Add(Volatile.Read(ref _bodyFallbackReasonCounts[i]));
        }

        return new MethodBodyReuseStatistics(
            succeeded ? MethodBodyReuseStatus.Succeeded : MethodBodyReuseStatus.Failed,
            Volatile.Read(ref _compiledBodyCount),
            Volatile.Read(ref _reuseAttemptCount),
            Volatile.Read(ref _reusedBodyCount),
            Volatile.Read(ref _fallbackBodyCount),
            globalFallbackReasonCounts.MoveToImmutable(),
            bodyFallbackReasonCounts.MoveToImmutable());
    }
}
