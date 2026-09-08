// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CompilerServer;

internal enum CompilationCacheAdmissionResult
{
    Stored,
    SkippedSmallInput,
    Disabled,
}

/// <summary>
/// Retains a bounded set of successful input compilations for the lifetime of the compiler server.
/// </summary>
internal sealed class CSharpCompilationCache
{
    internal const int DefaultMaxCacheSize = 10;
    internal const int DefaultMinimumSourceLength = 16_384;

    private readonly List<(string key, CSharpCompilation compilation, long sourceLength)> _cachedCompilations = new();
    private readonly object _cacheLock = new();
    private long _retainedSourceLength;
    private long _evictionCount;

    internal int MaxCacheSize { get; }
    internal int MinimumSourceLength { get; }

    internal CSharpCompilationCache(int maxCacheSize = DefaultMaxCacheSize, int minimumSourceLength = DefaultMinimumSourceLength)
    {
        Debug.Assert(maxCacheSize >= 0);
        Debug.Assert(minimumSourceLength >= 0);
        MaxCacheSize = maxCacheSize;
        MinimumSourceLength = minimumSourceLength;
    }

    internal CSharpCompilation? TryGetCompilation(string key)
        => AddOrUpdateMostRecentlyUsed(key, compilation: null, sourceLength: 0);

    internal CompilationCacheAdmissionResult CacheCompilation(string key, CSharpCompilation compilation)
    {
        if (MaxCacheSize == 0)
        {
            return CompilationCacheAdmissionResult.Disabled;
        }

        long sourceLength = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            sourceLength += tree.Length;
        }

        if (sourceLength < MinimumSourceLength)
        {
            RemoveCompilation(key);
            return CompilationCacheAdmissionResult.SkippedSmallInput;
        }

        AddOrUpdateMostRecentlyUsed(key, compilation, sourceLength);
        return CompilationCacheAdmissionResult.Stored;
    }

    internal (int EntryCount, long RetainedSourceLength, long EvictionCount) GetStatistics()
    {
        lock (_cacheLock)
        {
            return (_cachedCompilations.Count, _retainedSourceLength, _evictionCount);
        }
    }

    private void RemoveCompilation(string key)
    {
        lock (_cacheLock)
        {
            for (var index = 0; index < _cachedCompilations.Count; index++)
            {
                if (PathUtilities.Comparer.Equals(_cachedCompilations[index].key, key))
                {
                    _retainedSourceLength -= _cachedCompilations[index].sourceLength;
                    _cachedCompilations.RemoveAt(index);
                    break;
                }
            }
        }
    }

    private CSharpCompilation? AddOrUpdateMostRecentlyUsed(string key, CSharpCompilation? compilation, long sourceLength)
    {
        lock (_cacheLock)
        {
            var index = 0;
            for (; index < _cachedCompilations.Count; index++)
            {
                if (PathUtilities.Comparer.Equals(_cachedCompilations[index].key, key))
                {
                    var entry = _cachedCompilations[index];
                    if (compilation is null)
                    {
                        compilation = entry.compilation;
                        sourceLength = entry.sourceLength;
                    }

                    _retainedSourceLength -= entry.sourceLength;
                    _cachedCompilations.RemoveAt(index);
                    break;
                }
            }

            if (compilation is not null)
            {
                if (_cachedCompilations.Count == MaxCacheSize)
                {
                    var lastIndex = _cachedCompilations.Count - 1;
                    _retainedSourceLength -= _cachedCompilations[lastIndex].sourceLength;
                    _cachedCompilations.RemoveAt(lastIndex);
                    _evictionCount++;
                }

                _cachedCompilations.Insert(0, (key, compilation, sourceLength));
                _retainedSourceLength += sourceLength;
            }

            return compilation;
        }
    }
}
