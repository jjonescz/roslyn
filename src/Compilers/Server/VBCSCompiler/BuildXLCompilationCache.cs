// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET && BUILDXL_COMPILER_CACHE
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using Microsoft.CodeAnalysis.CommandLine;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    internal sealed class BuildXLCompilationCache(string cachePath) : ICompilationCache
    {
        private const byte FormatVersion = 1;
        private readonly string _root = Path.Combine(cachePath, "buildxl-v1");

        public bool TryRestoreCachedResult(string dllName, string hashKey, CompilationOutputFiles outputFiles, ICompilerServerLogger logger, DateTime outputTimestampUtc, CancellationToken cancellationToken)
            => WithSession(async (session, context, token) =>
            {
                var paths = GetOutputPaths(outputFiles);
                var mask = GetOutputMask(paths);
                var lookup = await session.GetContentHashListAsync(context, GetFingerprint(dllName, hashKey, mask), token).ConfigureAwait(false);
                if (!lookup.Succeeded)
                {
                    logger.Log($"BuildXL cache lookup failed: {lookup}");
                    return false;
                }

                var entry = lookup.ContentHashListWithDeterminism.ContentHashList;
                if (entry is null)
                {
                    return false;
                }

                if (entry.Payload is not { Count: 2 } || entry.Payload[0] != FormatVersion || entry.Payload[1] != mask)
                {
                    logger.Log("BuildXL cache entry has an incompatible output manifest.");
                    return false;
                }

                var expectedCount = 0;
                foreach (var path in paths)
                {
                    if (path is not null)
                        expectedCount++;
                }

                if (entry.Hashes.Count != expectedCount)
                {
                    logger.Log("BuildXL cache entry has an incomplete output manifest.");
                    return false;
                }

                var stagingDirectory = Path.Combine(_root, "staging", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDirectory);
                try
                {
                    var index = 0;
                    for (var role = 0; role < paths.Length; role++)
                    {
                        if (paths[role] is null)
                            continue;

                        var placed = await session.PlaceFileAsync(context, entry.Hashes[index++],
                            new AbsolutePath(Path.Combine(stagingDirectory, role.ToString())),
                            FileAccessMode.Write, FileReplacementMode.FailIfExists, FileRealizationMode.Copy, token).ConfigureAwait(false);
                        if (!placed.Succeeded)
                        {
                            logger.Log($"BuildXL cache content unavailable: {placed}");
                            return false;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    for (var role = 0; role < paths.Length; role++)
                    {
                        if (paths[role] is not { } targetPath)
                            continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                        File.Copy(Path.Combine(stagingDirectory, role.ToString()), targetPath, overwrite: true);
                        File.SetLastWriteTimeUtc(targetPath, outputTimestampUtc);
                    }
                }
                finally
                {
                    try
                    {
                        Directory.Delete(stagingDirectory, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.Log($"BuildXL cache staging cleanup failed: {ex.Message}");
                    }
                }

                logger.Log($"BuildXL cache hit: {dllName} [{hashKey}]");
                return true;
            }, fallback: false, logger, cancellationToken);

        public CompilationCacheStoreResult TryStoreResult(string dllName, string hashKey, CompilationOutputFiles outputFiles, string deterministicKey, ICompilerServerLogger logger, CancellationToken cancellationToken)
            => WithSession(async (session, context, token) =>
            {
                var paths = GetOutputPaths(outputFiles);
                var mask = GetOutputMask(paths);
                var hashes = new List<ContentHash>();
                foreach (var path in paths)
                {
                    if (path is null)
                        continue;

                    var put = await session.PutFileAsync(context, HashType.Vso0, new AbsolutePath(path), FileRealizationMode.Copy, token).ConfigureAwait(false);
                    if (!put.Succeeded)
                    {
                        logger.Log($"BuildXL cache content store failed: {put}");
                        return CompilationCacheStoreResult.Failed;
                    }

                    hashes.Add(put.ContentHash);
                }

                using var empty = new MemoryStream();
                var selector = await session.PutStreamAsync(context, HashType.Vso0, empty, token).ConfigureAwait(false);
                if (!selector.Succeeded)
                {
                    logger.Log($"BuildXL cache selector store failed: {selector}");
                    return CompilationCacheStoreResult.Failed;
                }

                var entry = new ContentHashList(hashes.ToArray(), [FormatVersion, mask]);
                var fingerprint = GetFingerprint(dllName, hashKey, mask);
                var previous = await session.GetContentHashListAsync(context, fingerprint, token).ConfigureAwait(false);
                if (!previous.Succeeded)
                {
                    logger.Log($"BuildXL cache lookup before publication failed: {previous}");
                    return CompilationCacheStoreResult.Failed;
                }

                if (previous.ContentHashListWithDeterminism.ContentHashList is { } previousEntry)
                {
                    if (!previousEntry.Equals(entry))
                    {
                        logger.Log($"BuildXL cache publication rejected conflicting outputs: {dllName} [{hashKey}]");
                        return CompilationCacheStoreResult.Failed;
                    }

                    return CompilationCacheStoreResult.SkippedExists;
                }

                var publish = await session.AddOrGetContentHashListAsync(context, fingerprint,
                    new ContentHashListWithDeterminism(entry, CacheDeterminism.None), token).ConfigureAwait(false);
                if (!publish.Succeeded)
                {
                    logger.Log($"BuildXL cache publication failed: {publish}");
                    return CompilationCacheStoreResult.Failed;
                }

                if (publish.ContentHashListWithDeterminism.ContentHashList is { } existing)
                {
                    if (!existing.Equals(entry))
                    {
                        logger.Log($"BuildXL cache publication rejected conflicting outputs: {dllName} [{hashKey}]");
                        return CompilationCacheStoreResult.Failed;
                    }

                    return CompilationCacheStoreResult.SkippedExists;
                }

                logger.Log($"BuildXL cache stored: {dllName} [{hashKey}]");
                return CompilationCacheStoreResult.Stored;
            }, CompilationCacheStoreResult.Failed, logger, cancellationToken);

        public void LogCacheMiss(string dllName, string hashKey, string deterministicKey, ICompilerServerLogger logger)
            => logger.Log($"BuildXL cache miss: {dllName} [{hashKey}]");

        private T WithSession<T>(Func<ICacheSession, Context, CancellationToken, Task<T>> action, T fallback, ICompilerServerLogger logger, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(_root);
                using var mutex = new ServerNamedMutex(
                    BuildServerConnection.GetServerMutexName("buildxl-cache." + CompilationCache.ComputeHashKey(_root.ToUpperInvariant())),
                    out var createdNew);
                try
                {
                    if (!createdNew && !mutex.TryLock(timeoutMs: 0))
                    {
                        logger.Log("BuildXL cache busy; skipping cache operation.");
                        return fallback;
                    }
                }
                catch (AbandonedMutexException)
                {
                    logger.Log("BuildXL cache lock was abandoned; reopening the local cache.");
                }

                // Keep the thread-affine mutex outside the asynchronous cache operations.
                return Task.Run(async () =>
                {
                    var context = new Context(NullLogger.Instance);
                    var root = new AbsolutePath(_root);
                    using var cache = LocalCache.CreateUnknownContentStoreInProcMemoizationStoreCache(
                        NullLogger.Instance, root,
                        new RocksDbMemoizationStoreConfiguration
                        {
                            Database = new RocksDbContentLocationDatabaseConfiguration(root / "memoization")
                            {
                                CleanOnInitialize = false,
                            },
                        },
                        LocalCacheConfiguration.CreateServerDisabled(),
                        new ConfigurationModel(ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1024)),
                        assumeCallerCreatesDirectoryForPlace: true);
                    var startup = await cache.StartupAsync(context).ConfigureAwait(false);
                    if (!startup.Succeeded)
                    {
                        logger.Log($"BuildXL cache startup failed: {startup}");
                        return fallback;
                    }

                    try
                    {
                        var created = cache.CreateSession(context, "roslyn", ImplicitPin.PutAndGet);
                        if (!created.Succeeded)
                        {
                            logger.Log($"BuildXL cache session creation failed: {created}");
                            return fallback;
                        }

                        using var session = created.Session;
                        var sessionStartup = await session.StartupAsync(context).ConfigureAwait(false);
                        if (!sessionStartup.Succeeded)
                        {
                            logger.Log($"BuildXL cache session startup failed: {sessionStartup}");
                            return fallback;
                        }

                        try
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            timeout.CancelAfter(TimeSpan.FromSeconds(30));
                            var result = await action(session, context, timeout.Token).ConfigureAwait(false);
                            timeout.Token.ThrowIfCancellationRequested();
                            return result;
                        }
                        finally
                        {
                            var shutdown = await session.ShutdownAsync(context).ConfigureAwait(false);
                            if (!shutdown.Succeeded)
                                logger.Log($"BuildXL cache session shutdown failed: {shutdown}");
                        }
                    }
                    finally
                    {
                        var shutdown = await cache.ShutdownAsync(context).ConfigureAwait(false);
                        if (!shutdown.Succeeded)
                            logger.Log($"BuildXL cache shutdown failed: {shutdown}");
                    }
                }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Log("BuildXL cache operation timed out; skipping cache operation.");
                return fallback;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CacheException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                logger.Log($"BuildXL cache operation failed: {ex.Message}");
                return fallback;
            }
        }

        private static string?[] GetOutputPaths(CompilationOutputFiles files)
            => [files.AssemblyPath, files.PdbPath, files.RefAssemblyPath, files.XmlDocPath];

        private static byte GetOutputMask(string?[] paths)
        {
            byte mask = 0;
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i] is not null)
                    mask |= (byte)(1 << i);
            }

            return mask;
        }

        private static StrongFingerprint GetFingerprint(string dllName, string hashKey, byte mask)
            => new(new Fingerprint(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"roslyn-output-cache/v{FormatVersion}\0{dllName}\0{hashKey}\0{mask}")))),
                new Selector(VsoHashInfo.Instance.EmptyHash));
    }
}
#endif
