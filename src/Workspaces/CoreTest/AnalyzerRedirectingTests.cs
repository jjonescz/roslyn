// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.UnitTests;

[UseExportProvider]
public class AnalyzerRedirectingTests
{
    [Fact]
    public void AnalyzerRedirecting()
    {
        using var workspace = new TestWorkspace();
        using var fixture = new AssemblyLoadTestFixture();

        var insertedAnalyzersDir = Path.Combine(fixture.TempDirectory, "DotNetAnalyzers");
        RedirectingAnalyzerAssemblyResolver.InsertedAnalyzersDirectory = insertedAnalyzersDir;

        // Create the mapping file.
        var mappingPath = Path.Combine(insertedAnalyzersDir, "mapping.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mappingPath)!);
        File.WriteAllText(mappingPath, """
            packs/Microsoft.NETCore.App.Ref/*/analyzers/dotnet/cs
            sdk/*/Sdks/Microsoft.NET.Sdk/analyzers
            sdk/*/Sdks/Microsoft.NET.Sdk.Web/analyzers/cs
            """);

        var provider = workspace.Services.GetRequiredService<IAnalyzerAssemblyLoaderProvider>();
        var loader = provider.GetShadowCopyLoader();

        // Add version 1 of the mock analyzer DLL into an SDK-like directory.
        var analyzerSdkDllPath = Path.Combine(fixture.TempDirectory, "dotnet/sdk/9.0.100-dev/Sdks/Microsoft.NET.Sdk/analyzers/Delta.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(analyzerSdkDllPath)!);
        File.Copy(fixture.Delta1, analyzerSdkDllPath);

        // Add version 2 of the mock analyzer DLL into a VS-like directory.
        var analyzerVsDllPath = Path.Combine(insertedAnalyzersDir, "sdk/9/Sdks/Microsoft.NET.Sdk/analyzers/Delta.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(analyzerVsDllPath)!);
        File.Copy(fixture.Delta2, analyzerVsDllPath);

        // Load the SDK analyzer.
        loader.AddDependencyLocation(analyzerSdkDllPath);
        var assembly = loader.LoadFromPath(analyzerSdkDllPath);

        // It should be redirected to the inserted analyzer.
        AssertEx.Equal("Delta, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null", assembly.GetName().ToString());
    }
}
