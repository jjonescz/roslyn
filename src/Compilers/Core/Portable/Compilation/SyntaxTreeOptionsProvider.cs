// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    public abstract class SyntaxTreeOptionsProvider
    {
        /// <summary>
        /// Get whether the given tree is generated.
        /// </summary>
        public abstract GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken);

        /// <summary>
        /// Get diagnostic severity setting for a given diagnostic identifier in a given tree.
        /// </summary>
        public abstract bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity);

        /// <summary>
        /// Get diagnostic severity set globally for a given diagnostic identifier
        /// </summary>
        public abstract bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity);
    }

    internal sealed class CompilerSyntaxTreeOptionsProvider : SyntaxTreeOptionsProvider
    {
        private readonly struct Options : IEquatable<Options>
        {
            public readonly GeneratedKind IsGenerated;
            public readonly ImmutableDictionary<string, ReportDiagnostic> DiagnosticOptions;

            public Options(AnalyzerConfigOptionsResult? result)
            {
                if (result is AnalyzerConfigOptionsResult r)
                {
                    DiagnosticOptions = r.TreeOptions;
                    IsGenerated = GeneratedCodeUtilities.GetGeneratedCodeKindFromOptions(r.AnalyzerOptions);
                }
                else
                {
                    DiagnosticOptions = SyntaxTree.EmptyDiagnosticOptions;
                    IsGenerated = GeneratedKind.Unknown;
                }
            }

            public bool Equals(Options other)
            {
                if (IsGenerated != other.IsGenerated ||
                    DiagnosticOptions.Count != other.DiagnosticOptions.Count)
                {
                    return false;
                }

                foreach (var pair in DiagnosticOptions)
                {
                    if (!other.DiagnosticOptions.TryGetValue(pair.Key, out var otherSeverity) ||
                        pair.Value != otherSeverity)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private readonly ImmutableDictionary<SyntaxTree, Options> _options;
        private readonly ImmutableArray<(string path, Options options)> _orderedOptions;

        private readonly AnalyzerConfigOptionsResult _globalOptions;

        public CompilerSyntaxTreeOptionsProvider(
            SyntaxTree?[] trees,
            ImmutableArray<AnalyzerConfigOptionsResult> results,
            AnalyzerConfigOptionsResult globalResults)
        {
            var builder = ImmutableDictionary.CreateBuilder<SyntaxTree, Options>();
            var orderedOptionsBuilder = ImmutableArray.CreateBuilder<(string path, Options options)>(trees.Length);
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i] != null)
                {
                    var options = new Options(results.IsDefault ? null : (AnalyzerConfigOptionsResult?)results[i]);
                    builder.Add(
                        trees[i]!,
                        options);
                    orderedOptionsBuilder.Add((trees[i]!.FilePath, options));
                }
            }
            _options = builder.ToImmutableDictionary();
            _orderedOptions = orderedOptionsBuilder.ToImmutable();
            _globalOptions = globalResults;
        }

        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken _)
            => _options.TryGetValue(tree, out var value) ? value.IsGenerated : GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken _, out ReportDiagnostic severity)
        {
            if (_options.TryGetValue(tree, out var value))
            {
                return value.DiagnosticOptions.TryGetValue(diagnosticId, out severity);
            }
            severity = ReportDiagnostic.Default;
            return false;
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken _, out ReportDiagnostic severity)
        {
            if (_globalOptions.TreeOptions is object)
            {
                return _globalOptions.TreeOptions.TryGetValue(diagnosticId, out severity);
            }
            severity = ReportDiagnostic.Default;
            return false;
        }

        internal bool IsEquivalentTo(CompilerSyntaxTreeOptionsProvider other)
        {
            if (_orderedOptions.Length != other._orderedOptions.Length ||
                !HaveEquivalentDiagnosticOptions(_globalOptions.TreeOptions, other._globalOptions.TreeOptions))
            {
                return false;
            }

            for (var i = 0; i < _orderedOptions.Length; i++)
            {
                var left = _orderedOptions[i];
                var right = other._orderedOptions[i];
                if (!string.Equals(left.path, right.path, StringComparison.Ordinal) ||
                    !left.options.Equals(right.options))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HaveEquivalentDiagnosticOptions(
            ImmutableDictionary<string, ReportDiagnostic>? left,
            ImmutableDictionary<string, ReportDiagnostic>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var rightSeverity) ||
                    pair.Value != rightSeverity)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
