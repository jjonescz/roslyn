// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.CodeAnalysis.CommandLine;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    internal interface ICompilationCache
    {
        bool TryRestoreCachedResult(string dllName, string hashKey, CompilationOutputFiles outputFiles, ICompilerServerLogger logger, DateTime outputTimestampUtc, CancellationToken cancellationToken);

        CompilationCacheStoreResult TryStoreResult(string dllName, string hashKey, CompilationOutputFiles outputFiles, string deterministicKey, ICompilerServerLogger logger, CancellationToken cancellationToken);

        void LogCacheMiss(string dllName, string hashKey, string deterministicKey, ICompilerServerLogger logger);
    }
}
