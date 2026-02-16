// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal partial class Binder
    {
        /// <summary>
        /// True if we are currently in an unsafe region (type, member, or block).
        /// </summary>
        /// <remarks>
        /// Does not imply that this compilation allows unsafe regions (could be in an error recovery scenario).
        /// To determine that, check this.Compilation.Options.AllowUnsafe.
        /// </remarks>
        internal bool InUnsafeRegion
        {
            get { return this.Flags.Includes(BinderFlags.UnsafeRegion); }
        }

        internal void ReportDiagnosticsIfUnsafeMemberAccess(BindingDiagnosticBag diagnostics, Symbol symbol, SyntaxNodeOrToken node)
        {
            if (diagnostics.DiagnosticBag is { } bag)
            {
                ReportDiagnosticsIfUnsafeMemberAccess(bag, symbol, node);
            }
        }

        internal void ReportDiagnosticsIfUnsafeMemberAccess(DiagnosticBag diagnostics, Symbol symbol, SyntaxNodeOrToken node)
        {
            ReportDiagnosticsIfUnsafeMemberAccess(diagnostics, symbol, node, forConstructorConstraint: false);

            switch (symbol)
            {
                case MethodSymbol methodSymbol:
                    {
                        var arity = methodSymbol.GetMemberArityIncludingExtension();
                        if (arity != 0)
                        {
                            var typeParameters = methodSymbol.GetTypeParametersIncludingExtension();
                            var typeArguments = methodSymbol.ContainingType.TypeArgumentsWithAnnotationsNoUseSiteDiagnostics.Concat(methodSymbol.TypeArgumentsWithAnnotations);
                            for (int i = 0; i < arity; i++)
                            {
                                var typeParameter = typeParameters[i];
                                if (typeParameter.HasConstructorConstraint &&
                                    typeArguments[i].Type is NamedTypeSymbol typeArgument)
                                {
                                    checkTypeArgumentWithConstructorConstraint(this, typeArgument, node, diagnostics);
                                }
                            }
                        }
                    }
                    break;

                case NamedTypeSymbol typeSymbol:
                    {
                        var arity = typeSymbol.TypeParameters.Length;
                        for (int i = 0; i < arity; i++)
                        {
                            var typeParameter = typeSymbol.TypeParameters[i];
                            if (typeParameter.HasConstructorConstraint &&
                                typeSymbol.TypeArgumentsWithAnnotationsNoUseSiteDiagnostics[i].Type is NamedTypeSymbol typeArgument)
                            {
                                checkTypeArgumentWithConstructorConstraint(this, typeArgument, node, diagnostics);
                            }
                        }
                    }
                    break;
            }

            static void checkTypeArgumentWithConstructorConstraint(Binder @this, NamedTypeSymbol typeArgument, SyntaxNodeOrToken node, DiagnosticBag diagnostics)
            {
                foreach (var ctor in typeArgument.InstanceConstructors)
                {
                    if (ctor.ParameterCount == 0)
                    {
                        @this.ReportDiagnosticsIfUnsafeMemberAccess(diagnostics, ctor, node, forConstructorConstraint: true);
                        break;
                    }
                }
            }
        }

        private void ReportDiagnosticsIfUnsafeMemberAccess(DiagnosticBag diagnostics, Symbol symbol, SyntaxNodeOrToken node, bool forConstructorConstraint)
        {
            var callerUnsafeMode = symbol.CallerUnsafeMode;
            if (callerUnsafeMode != CallerUnsafeMode.None)
            {
                Debug.Assert(callerUnsafeMode == CallerUnsafeMode.Explicit || !forConstructorConstraint);
                ReportUnsafeIfNotAllowed(node, diagnostics, disallowedUnder: MemorySafetyRules.Updated,
                    customErrorCode: callerUnsafeMode switch
                    {
                        CallerUnsafeMode.Explicit => forConstructorConstraint ? ErrorCode.ERR_UnsafeConstructorConstraint : ErrorCode.ERR_UnsafeMemberOperation,
                        CallerUnsafeMode.Implicit => ErrorCode.ERR_UnsafeMemberOperationCompat,
                        _ => throw ExceptionUtilities.UnexpectedValue(callerUnsafeMode),
                    },
                    customArgs: [symbol]);
            }
        }

        /// <summary>
        /// If this fails, call <see cref="ReportDiagnosticsIfUnsafeMemberAccess(BindingDiagnosticBag, Symbol, SyntaxNodeOrToken)"/> for the <paramref name="symbol"/> instead and add corresponding tests.
        /// </summary>
        [Conditional("DEBUG")]
        internal static void AssertNotUnsafeMemberAccess(Symbol symbol)
        {
            // Resolving `symbol.ToString()` can lead to errors in some cases
            // and `Debug.Assert` on .NET Framework evaluates all interpolated values eagerly,
            // so avoid evaluating that unless we are going to fail anyway.

            if (symbol.CallerUnsafeMode is not CallerUnsafeMode.None)
            {
                Debug.Fail($"Symbol {symbol} has {nameof(symbol.CallerUnsafeMode)}={symbol.CallerUnsafeMode}.");
            }

            if (symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event)
            {
                Debug.Fail($"Symbol {symbol} has {nameof(symbol.Kind)}={symbol.Kind}.");
            }

            if (symbol is NamedTypeSymbol { TypeParameters.Length: > 0 })
            {
                Debug.Fail($"Symbol {symbol} is a generic type.");
            }
        }

        /// <inheritdoc cref="ReportUnsafeIfNotAllowed(SyntaxNodeOrToken, DiagnosticBag, MemorySafetyRules, TypeSymbol?, ErrorCode?, object[])"/>
        internal bool ReportUnsafeIfNotAllowed(
            SyntaxNodeOrToken node,
            BindingDiagnosticBag diagnostics,
            MemorySafetyRules disallowedUnder,
            TypeSymbol? sizeOfTypeOpt = null,
            ErrorCode? customErrorCode = null,
            object[]? customArgs = null)
        {
            return diagnostics.DiagnosticBag is { } bag &&
                ReportUnsafeIfNotAllowed(node, bag, disallowedUnder, sizeOfTypeOpt, customErrorCode, customArgs);
        }

        /// <param name="disallowedUnder">
        /// Memory safety rules which the current location is disallowed under.
        /// </param>
        /// <returns>True if a diagnostic was reported</returns>
        internal bool ReportUnsafeIfNotAllowed(
            SyntaxNodeOrToken node,
            DiagnosticBag diagnostics,
            MemorySafetyRules disallowedUnder,
            TypeSymbol? sizeOfTypeOpt = null,
            ErrorCode? customErrorCode = null,
            object[]? customArgs = null)
        {
            Debug.Assert((node.Kind() == SyntaxKind.SizeOfExpression) == ((object?)sizeOfTypeOpt != null), "Should have a type for (only) sizeof expressions.");
            var diagnosticInfo = GetUnsafeDiagnosticInfo(disallowedUnder, sizeOfTypeOpt, customErrorCode, customArgs);
            if (diagnosticInfo == null)
            {
                return false;
            }

            diagnostics.Add(new CSDiagnostic(diagnosticInfo, node.GetLocation()));
            return true;
        }

        /// <inheritdoc cref="ReportUnsafeIfNotAllowed(SyntaxNodeOrToken, DiagnosticBag, MemorySafetyRules, TypeSymbol?, ErrorCode?, object[])"/>
        internal bool ReportUnsafeIfNotAllowed(Location location, BindingDiagnosticBag diagnostics, MemorySafetyRules disallowedUnder)
        {
            return diagnostics.DiagnosticBag is { } bag &&
                ReportUnsafeIfNotAllowed(location, bag, disallowedUnder);
        }

        /// <inheritdoc cref="ReportUnsafeIfNotAllowed(SyntaxNodeOrToken, DiagnosticBag, MemorySafetyRules, TypeSymbol?, ErrorCode?, object[])"/>
        internal bool ReportUnsafeIfNotAllowed(Location location, DiagnosticBag diagnostics, MemorySafetyRules disallowedUnder)
        {
            var diagnosticInfo = GetUnsafeDiagnosticInfo(disallowedUnder, sizeOfTypeOpt: null);
            if (diagnosticInfo == null)
            {
                return false;
            }

            diagnostics.Add(new CSDiagnostic(diagnosticInfo, location));
            return true;
        }

        private CSDiagnosticInfo? GetUnsafeDiagnosticInfo(
            MemorySafetyRules disallowedUnder,
            TypeSymbol? sizeOfTypeOpt,
            ErrorCode? customErrorCode = null,
            object[]? customArgs = null)
        {
            Debug.Assert(sizeOfTypeOpt is null || disallowedUnder is MemorySafetyRules.Legacy);

            if (this.Flags.Includes(BinderFlags.SuppressUnsafeDiagnostics))
            {
                return null;
            }
            else if (!this.InUnsafeRegion)
            {
                if (disallowedUnder is MemorySafetyRules.Legacy)
                {
                    Debug.Assert(customErrorCode is null && customArgs is null);

                    if (this.Compilation.SourceModule.UseUpdatedMemorySafetyRules)
                    {
                        return MessageID.IDS_FeatureUnsafeEvolution.GetFeatureAvailabilityDiagnosticInfo(this.Compilation);
                    }

                    return ((object?)sizeOfTypeOpt == null)
                        ? new CSDiagnosticInfo(ErrorCode.ERR_UnsafeNeeded)
                        : new CSDiagnosticInfo(ErrorCode.ERR_SizeofUnsafe, sizeOfTypeOpt);
                }

                Debug.Assert(disallowedUnder is MemorySafetyRules.Updated);

                if (this.Compilation.SourceModule.UseUpdatedMemorySafetyRules)
                {
                    return MessageID.IDS_FeatureUnsafeEvolution.GetFeatureAvailabilityDiagnosticInfo(this.Compilation)
                        ?? new CSDiagnosticInfo(customErrorCode ?? ErrorCode.ERR_UnsafeOperation, customArgs ?? []);
                }

                // This location is disallowed only under updated memory safety rules which are not enabled.
                // We report an error elsewhere, usually at the pointer type itself
                // (where we are called with `disallowedUnder: MemorySafetyRules.Legacy`).
                return null;
            }
            else if (this.IsIndirectlyInIterator && MessageID.IDS_FeatureRefUnsafeInIteratorAsync.GetFeatureAvailabilityDiagnosticInfo(Compilation) is { } unsafeInIteratorDiagnosticInfo)
            {
                if (disallowedUnder is MemorySafetyRules.Legacy)
                {
                    return unsafeInIteratorDiagnosticInfo;
                }

                // This location is disallowed only under updated memory safety rules.
                // We report the RefUnsafeInIteratorAsync langversion error elsewhere, usually at the pointer type itself
                // (where we are called with `disallowedUnder: MemorySafetyRules.Legacy`).
                Debug.Assert(disallowedUnder is MemorySafetyRules.Updated);
                return null;
            }
            else
            {
                return null;
            }
        }
    }

    internal enum MemorySafetyRules
    {
        Legacy,

        /// <summary>
        /// <see cref="CSharpCompilationOptions.UseUpdatedMemorySafetyRules"/>
        /// </summary>
        Updated,
    }
}
