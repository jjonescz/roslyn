// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.CodeAnalysis.Utilities;

/// <summary>
/// Provides path canonicalization to resolve the actual filesystem casing of a path.
/// This is used in the IDE layer to ensure that editorconfig file paths and source file paths
/// have consistent casing, even when they originate from different project system components
/// (e.g., MSBuild's GetPathsOfAllDirectoriesAbove vs. Compile items).
/// </summary>
internal static class PathCanonicalization
{
    /// <summary>
    /// Attempts to resolve the canonical filesystem casing for the given file or directory path.
    /// On Windows, this walks the path components and uses <c>FindFirstFile</c> to obtain
    /// the actual casing from the filesystem for each component. Unlike <c>GetFinalPathNameByHandle</c>,
    /// this preserves the original volume/drive letter (important for subst drives and junctions).
    /// On non-Windows platforms, returns the path unchanged (filesystems are typically case-sensitive).
    /// If the file does not exist or the operation fails, returns the original path.
    /// </summary>
    [return: NotNullIfNotNull(nameof(path))]
    internal static string? GetCanonicalPath(string? path)
    {
        if (path is null || path.IndexOf('\0') >= 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return path;
        }

        return GetCanonicalCasingWindows(path);
    }

    private static string GetCanonicalCasingWindows(string path)
    {
        // Normalize the path to remove relative segments (. and ..) before walking components.
        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }

        // Extract the root (e.g., "C:\", "\\server\share\") — we preserve its original casing.
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return path;
        }

        // Walk the remaining path components, resolving each one's actual casing via FindFirstFile.
        var remaining = path.Substring(root.Length);
        if (remaining.Length == 0)
        {
            return path;
        }

        var components = remaining.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var component in components)
        {
            var searchPath = Path.Combine(current, component);
            var findData = new WIN32_FIND_DATAW();
            var findHandle = FindFirstFileW(searchPath, ref findData);
            if (findHandle == INVALID_HANDLE_VALUE)
            {
                // Component doesn't exist on disk — return original path.
                return path;
            }

            FindClose(findHandle);
            current = Path.Combine(current, findData.cFileName);
        }

        return current;
    }

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, ref WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr hFindFile);
}
