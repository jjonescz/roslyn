// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CompilerServer;

/// <summary>
/// Retains a bounded set of successful C# emit baselines for the lifetime of the compiler server.
/// </summary>
internal sealed class CSharpMethodBodyReuseCache
{
    private const int DefaultMaxCacheSize = 10;

    private readonly (string key, IMethodBodyReuse reuse)[] _cachedReuses;
    private readonly object _cacheLock = new();
    private int _cacheSize;

    internal CSharpMethodBodyReuseCache(int maxCacheSize = DefaultMaxCacheSize)
    {
        Debug.Assert(maxCacheSize > 0);
        _cachedReuses = new (string, IMethodBodyReuse)[maxCacheSize];
    }

    internal IMethodBodyReuse? TryGetReuse(string key)
        => AddOrUpdateMostRecentlyUsed(key, reuse: null);

    internal void CacheReuse(string key, IMethodBodyReuse reuse)
        => AddOrUpdateMostRecentlyUsed(key, reuse);

    private IMethodBodyReuse? AddOrUpdateMostRecentlyUsed(string key, IMethodBodyReuse? reuse)
    {
        lock (_cacheLock)
        {
            var index = 0;
            for (; index < _cacheSize; index++)
            {
                if (PathUtilities.Comparer.Equals(_cachedReuses[index].key, key))
                {
                    reuse ??= _cachedReuses[index].reuse;
                    break;
                }
            }

            if (reuse is not null)
            {
                var maxCacheSize = _cachedReuses.Length;
                var insertionIndex = Math.Min(index, maxCacheSize - 1);
                for (; insertionIndex > 0; insertionIndex--)
                {
                    _cachedReuses[insertionIndex] = _cachedReuses[insertionIndex - 1];
                }

                _cachedReuses[0] = (key, reuse);
                if (index == _cacheSize)
                {
                    _cacheSize = Math.Min(maxCacheSize, _cacheSize + 1);
                }
            }

            return reuse;
        }
    }
}
