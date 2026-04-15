// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CommandLine;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CompilerServer.UnitTests
{
    public class CompilationCacheTrackerTests : TestBase
    {
        private readonly ICompilerServerLogger _logger;

        public CompilationCacheTrackerTests(ITestOutputHelper testOutputHelper)
        {
            _logger = new XunitCompilerServerLogger(testOutputHelper);
        }

        [Fact]
        public void RecordUsedEntry_TracksEntries()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            tracker.RecordUsedEntry(cacheDir, "A.dll", "hash1");
            tracker.RecordUsedEntry(cacheDir, "B.dll", "hash2");

            // No errors, entries are tracked (verified via purge below).
        }

        [Fact]
        public void PurgeUnusedEntries_NoCacheRoots_ReturnsMessage()
        {
            var tracker = new CompilationCacheTracker();
            var result = tracker.PurgeUnusedEntries(_logger);
            Assert.Contains("No cache paths have been observed", result);
        }

        [Fact]
        public void PurgeUnusedEntries_DeletesUnusedEntries()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            // Create cache entries on disk
            var usedEntry = Path.Combine(cacheDir, "Used.dll", "hash_used");
            var unusedEntry = Path.Combine(cacheDir, "Unused.dll", "hash_unused");
            Directory.CreateDirectory(usedEntry);
            File.WriteAllBytes(Path.Combine(usedEntry, "assembly"), [1, 2, 3]);
            Directory.CreateDirectory(unusedEntry);
            File.WriteAllBytes(Path.Combine(unusedEntry, "assembly"), [4, 5, 6]);

            // Track only the used entry
            tracker.RecordUsedEntry(cacheDir, "Used.dll", "hash_used");

            var result = tracker.PurgeUnusedEntries(_logger);

            Assert.Contains("Deleted: 1", result);
            Assert.Contains("Kept: 1", result);
            Assert.True(Directory.Exists(usedEntry));
            Assert.False(Directory.Exists(unusedEntry));
        }

        [Fact]
        public void PurgeUnusedEntries_KeepsAllUsedEntries()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            var entry1 = Path.Combine(cacheDir, "A.dll", "hash1");
            var entry2 = Path.Combine(cacheDir, "B.dll", "hash2");
            Directory.CreateDirectory(entry1);
            File.WriteAllBytes(Path.Combine(entry1, "assembly"), [1]);
            Directory.CreateDirectory(entry2);
            File.WriteAllBytes(Path.Combine(entry2, "assembly"), [2]);

            tracker.RecordUsedEntry(cacheDir, "A.dll", "hash1");
            tracker.RecordUsedEntry(cacheDir, "B.dll", "hash2");

            var result = tracker.PurgeUnusedEntries(_logger);

            Assert.Contains("Deleted: 0", result);
            Assert.Contains("Kept: 2", result);
            Assert.True(Directory.Exists(entry1));
            Assert.True(Directory.Exists(entry2));
        }

        [Fact]
        public void PurgeUnusedEntries_RemovesEmptyDllDirectories()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            // Create a single unused entry
            var dllDir = Path.Combine(cacheDir, "Orphan.dll");
            var entryDir = Path.Combine(dllDir, "hash_orphan");
            Directory.CreateDirectory(entryDir);
            File.WriteAllBytes(Path.Combine(entryDir, "assembly"), [1]);

            // Record a different entry so the cache root is known
            var usedDir = Path.Combine(cacheDir, "Used.dll", "hash_used");
            Directory.CreateDirectory(usedDir);
            File.WriteAllBytes(Path.Combine(usedDir, "assembly"), [2]);
            tracker.RecordUsedEntry(cacheDir, "Used.dll", "hash_used");

            var result = tracker.PurgeUnusedEntries(_logger);

            Assert.Contains("Deleted: 1", result);
            Assert.False(Directory.Exists(dllDir), "Empty DLL directory should be removed");
            Assert.True(Directory.Exists(usedDir));
        }

        [Fact]
        public void PurgeUnusedEntries_SkipsStagingDirectories()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            // Create a staging directory (ends with .tmp)
            var dllDir = Path.Combine(cacheDir, "Lib.dll");
            var stagingDir = Path.Combine(dllDir, "somehash.abc123.tmp");
            Directory.CreateDirectory(stagingDir);

            // Create a used entry so the cache root is known
            var usedDir = Path.Combine(dllDir, "hash_used");
            Directory.CreateDirectory(usedDir);
            File.WriteAllBytes(Path.Combine(usedDir, "assembly"), [1]);
            tracker.RecordUsedEntry(cacheDir, "Lib.dll", "hash_used");

            var result = tracker.PurgeUnusedEntries(_logger);

            Assert.Contains("Deleted: 0", result);
            Assert.True(Directory.Exists(stagingDir), "Staging directories should not be deleted");
        }

        [Fact]
        public void PurgeUnusedEntries_HandlesMixedUsedAndUnused()
        {
            var tracker = new CompilationCacheTracker();
            var cacheDir = Temp.CreateDirectory().Path;

            // Same DLL, two hash entries - one used, one not
            var dllDir = Path.Combine(cacheDir, "Lib.dll");
            var usedEntry = Path.Combine(dllDir, "hash_current");
            var staleEntry = Path.Combine(dllDir, "hash_old");
            Directory.CreateDirectory(usedEntry);
            File.WriteAllBytes(Path.Combine(usedEntry, "assembly"), [1]);
            Directory.CreateDirectory(staleEntry);
            File.WriteAllBytes(Path.Combine(staleEntry, "assembly"), [2]);

            tracker.RecordUsedEntry(cacheDir, "Lib.dll", "hash_current");

            var result = tracker.PurgeUnusedEntries(_logger);

            Assert.Contains("Deleted: 1", result);
            Assert.Contains("Kept: 1", result);
            Assert.True(Directory.Exists(usedEntry));
            Assert.False(Directory.Exists(staleEntry));
            // DLL directory should still exist because usedEntry is still there
            Assert.True(Directory.Exists(dllDir));
        }
    }
}
