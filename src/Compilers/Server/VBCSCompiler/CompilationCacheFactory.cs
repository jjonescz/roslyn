// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CommandLine;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CompilerServer
{
    internal static class CompilationCacheFactory
    {
        internal const string UseBuildXLCacheFeatureFlag = "use-buildxl-cache";

        internal static ICompilationCache? TryCreate(CommandLineArguments arguments, ICompilerServerLogger logger)
        {
            if (!arguments.ParseOptions.Features.TryGetValue(UseBuildXLCacheFeatureFlag, out var path))
            {
                return CompilationCache.TryCreate(arguments, logger);
            }

#if NET && BUILDXL_COMPILER_CACHE
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                logger.Log("BuildXL compilation cache disabled: the prototype requires Windows x64.");
                return null;
            }

            if (!arguments.CompilationOptions.Deterministic)
            {
                logger.Log("BuildXL compilation cache disabled: deterministic compilation is required.");
                return null;
            }

            if (string.IsNullOrEmpty(path) || !PathUtilities.IsAbsolute(path))
            {
                logger.Log("BuildXL compilation cache disabled: use-buildxl-cache requires an absolute cache path.");
                return null;
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                logger.Log($"BuildXL compilation cache disabled: invalid cache path: {ex.Message}");
                return null;
            }

            logger.Log($"BuildXL compilation cache enabled at: {path}");
            return new BuildXLCompilationCache(path);
#else
            logger.Log("BuildXL compilation cache disabled: build with EnableBuildXLCompilerCachePrototype=true to include the prototype.");
            return null;
#endif
        }
    }
}
