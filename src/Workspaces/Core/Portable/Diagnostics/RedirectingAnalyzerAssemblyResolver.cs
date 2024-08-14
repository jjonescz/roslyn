// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics;

[Export(typeof(IAnalyzerAssemblyResolver)), Shared]
internal sealed class RedirectingAnalyzerAssemblyResolver : IAnalyzerAssemblyResolver
{
    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public RedirectingAnalyzerAssemblyResolver() { }

    // TODO: This should somehow point to the location in VS where the analyzers will be inserted.
    public static string InsertedAnalyzersDirectory { get; set; } = "";

    public Assembly? ResolveAssembly(AssemblyName assemblyName, string assemblyOriginalDirectory)
    {
        if (PathUtilities.NormalizeWithForwardSlash(assemblyOriginalDirectory).IndexOf(
                "/dotnet/sdk/9.0.100-dev/Sdks/Microsoft.NET.Sdk/analyzers",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var redirectedPath = Path.Combine(InsertedAnalyzersDirectory, "sdk/9/Sdks/Microsoft.NET.Sdk/analyzers", assemblyName.Name + ".dll");
            return Assembly.LoadFile(redirectedPath);
        }

        return null;
    }
}
