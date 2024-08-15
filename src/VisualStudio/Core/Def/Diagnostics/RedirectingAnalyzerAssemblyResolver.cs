// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Diagnostics;

[Export(typeof(IAnalyzerAssemblyResolver)), Shared]
internal sealed class RedirectingAnalyzerAssemblyResolver : IAnalyzerAssemblyResolver
{
    private readonly Lazy<ImmutableArray<Matcher>> _matchers;

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public RedirectingAnalyzerAssemblyResolver()
    {
        _matchers = new(CreateMatchers);
    }

    // TODO: This should somehow point to the location in VS where the analyzers will be inserted.
    public static string InsertedAnalyzersDirectory { get; set; } = "";

    public Assembly? ResolveAssembly(AssemblyName assemblyName, string assemblyOriginalDirectory)
    {
        foreach (var matcher in _matchers.Value)
        {
            if (matcher.TryRedirect(assemblyOriginalDirectory) is { } redirectedDirectory)
            {
                var redirectedPath = Path.Combine(InsertedAnalyzersDirectory, redirectedDirectory, assemblyName.Name + ".dll");
                return Assembly.LoadFile(redirectedPath);
            }
        }

        return null;
    }

    private ImmutableArray<Matcher> CreateMatchers()
    {
        var mappingFilePath = Path.Combine(InsertedAnalyzersDirectory, "mapping.txt");

        if (!File.Exists(mappingFilePath))
        {
            return [];
        }

        var mappings = File.ReadAllLines(mappingFilePath);
        var builder = ArrayBuilder<Matcher>.GetInstance(mappings.Length);

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping) ||
                mapping.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = PathUtilities.NormalizeWithForwardSlash(mapping);

            if (normalized.IndexOf("/*/", StringComparison.Ordinal) is var starIndex and >= 0)
            {
                var prefix = normalized[..starIndex];
                var suffix = normalized[(starIndex + 3)..];
                builder.Add(new Matcher { Prefix = prefix, Suffix = suffix });
                continue;
            }

            builder.Add(new Matcher { Prefix = normalized });
        }

        return builder.ToImmutableAndFree();
    }

    private readonly record struct Matcher
    {
        public required string Prefix { get; init; }
        public string? Suffix { get; init; }

        public string? TryRedirect(string directory)
        {
            directory = PathUtilities.NormalizeWithForwardSlash(directory);

            // TODO: Go over all matching prefixes.
            if (directory.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase) is var prefixStart and > 0 &&
                directory[prefixStart - 1] == '/')
            {
                if (Suffix is null)
                {
                    if (directory.Length == prefixStart + Prefix.Length || directory[prefixStart + Prefix.Length + 1] == '/')
                    {
                        return Prefix;
                    }
                }
                else
                {
                    if (directory.AsSpan(prefixStart + Prefix.Length) is ['/', ..] &&
                        directory.IndexOf('/', prefixStart + Prefix.Length + 1) is var suffixStart and >= 0 &&
                        directory.AsSpan(suffixStart + 1).StartsWith(Suffix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        var versionStart = prefixStart + Prefix.Length + 1;
                        var version = directory.AsSpan(versionStart, suffixStart - versionStart);

                        if (version.IndexOf('.') is var dotIndex and >= 0)
                        {
                            return Prefix + '/' + version[..dotIndex].ToString() + '/' + Suffix;
                        }
                    }
                }
            }

            return null;
        }
    }
}
