// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CommandLine;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    /// <summary>
    /// Tracks which compilation cache entries have been used (restored or stored)
    /// during the lifetime of the compiler server. This information is used by the
    /// <c>-purgecache</c> command to delete unused entries.
    /// </summary>
    internal sealed class CompilationCacheTracker
    {
        /// <summary>
        /// Set of all cache root paths that have been observed.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _cacheRoots = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set of used cache entry directory paths (full paths of the form <c>&lt;cacheRoot&gt;/&lt;dllName&gt;/&lt;hashKey&gt;</c>).
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _usedEntries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Records that a cache entry was used (either a cache hit or a new store).
        /// </summary>
        internal void RecordUsedEntry(string cachePath, string dllName, string hashKey)
        {
            _cacheRoots.TryAdd(cachePath, 0);
            var entryDir = Path.Combine(cachePath, dllName, hashKey);
            _usedEntries.TryAdd(entryDir, 0);
        }

        /// <summary>
        /// Deletes all cache entries that were not used during this server's lifetime.
        /// Returns a human-readable summary of the purge operation.
        /// </summary>
        internal string PurgeUnusedEntries(ICompilerServerLogger logger)
        {
            var cacheRoots = _cacheRoots.Keys.ToArray();
            if (cacheRoots.Length == 0)
            {
                return "No cache paths have been observed by this server. Nothing to purge.";
            }

            var totalDeleted = 0;
            var totalKept = 0;
            var totalErrors = 0;
            var details = new StringBuilder();

            foreach (var cacheRoot in cacheRoots)
            {
                if (!Directory.Exists(cacheRoot))
                {
                    continue;
                }

                try
                {
                    // Enumerate <cacheRoot>/<dllName>/ directories
                    foreach (var dllDir in Directory.EnumerateDirectories(cacheRoot))
                    {
                        var dllName = Path.GetFileName(dllDir);

                        // Enumerate <cacheRoot>/<dllName>/<hashKey>/ directories
                        foreach (var entryDir in Directory.EnumerateDirectories(dllDir))
                        {
                            var dirName = Path.GetFileName(entryDir);

                            // Skip staging directories (they end with .tmp)
                            if (dirName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (_usedEntries.ContainsKey(entryDir))
                            {
                                totalKept++;
                            }
                            else
                            {
                                try
                                {
                                    Directory.Delete(entryDir, recursive: true);
                                    totalDeleted++;
                                    logger.Log($"Cache purge: deleted {dllName}/{dirName}");
                                }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                {
                                    totalErrors++;
                                    logger.Log($"Cache purge: failed to delete {dllName}/{dirName}: {ex.Message}");
                                }
                            }
                        }

                        // Remove the dllName directory if it's now empty
                        try
                        {
                            if (Directory.Exists(dllDir) && !Directory.EnumerateFileSystemEntries(dllDir).Any())
                            {
                                Directory.Delete(dllDir);
                                logger.Log($"Cache purge: removed empty directory {dllName}");
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Best effort
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.Log($"Cache purge: error enumerating {cacheRoot}: {ex.Message}");
                    details.AppendLine($"Error enumerating {cacheRoot}: {ex.Message}");
                }
            }

            var summary = $"Cache purge complete. Deleted: {totalDeleted}, Kept: {totalKept}, Errors: {totalErrors}";
            logger.Log(summary);

            if (details.Length > 0)
            {
                return summary + Environment.NewLine + details.ToString().TrimEnd();
            }

            return summary;
        }
    }
}
