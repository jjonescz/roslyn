// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CompilerServer;

/// <summary>
/// Retains a bounded set of successful input compilations for the lifetime of the compiler server.
/// </summary>
internal sealed class CSharpCompilationCache
{
    private const int DefaultMaxCacheSize = 10;

    private readonly (string key, CSharpCompilation compilation)[] _cachedCompilations;
    private readonly object _cacheLock = new();
    private int _cacheSize;

    internal CSharpCompilationCache(int maxCacheSize = DefaultMaxCacheSize)
    {
        Debug.Assert(maxCacheSize > 0);
        _cachedCompilations = new (string, CSharpCompilation)[maxCacheSize];
    }

    internal CSharpCompilation? TryGetCompilation(string key)
        => AddOrUpdateMostRecentlyUsed(key, compilation: null);

    internal void CacheCompilation(string key, CSharpCompilation compilation)
        => AddOrUpdateMostRecentlyUsed(key, compilation);

    private CSharpCompilation? AddOrUpdateMostRecentlyUsed(string key, CSharpCompilation? compilation)
    {
        lock (_cacheLock)
        {
            var index = 0;
            for (; index < _cacheSize; index++)
            {
                if (PathUtilities.Comparer.Equals(_cachedCompilations[index].key, key))
                {
                    compilation ??= _cachedCompilations[index].compilation;
                    break;
                }
            }

            if (compilation is not null)
            {
                var maxCacheSize = _cachedCompilations.Length;
                var insertionIndex = Math.Min(index, maxCacheSize - 1);
                for (; insertionIndex > 0; insertionIndex--)
                {
                    _cachedCompilations[insertionIndex] = _cachedCompilations[insertionIndex - 1];
                }

                _cachedCompilations[0] = (key, compilation);
                if (index == _cacheSize)
                {
                    _cacheSize = Math.Min(maxCacheSize, _cacheSize + 1);
                }
            }

            return compilation;
        }
    }
}
