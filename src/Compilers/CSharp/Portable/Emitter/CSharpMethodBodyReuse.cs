// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.Debugging;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Emit;

/// <summary>
/// Reuses ordinary method bodies from an earlier in-memory compilation while the current
/// compilation continues through the normal full-module emit pipeline.
/// </summary>
/// <remarks>
/// This initial implementation deliberately falls back to compiling bodies with locals,
/// exception handlers, imports, synthesized-method debug information, instrumentation,
/// fields in the compilation, diagnostics from the previous emit, or declaration changes
/// anywhere in the compilation.
/// </remarks>
internal sealed class CSharpMethodBodyReuse : IMethodBodyReuse
{
    private readonly CSharpCompilation _previousCompilation;
    private readonly PEModuleBuilder _previousModuleBuilder;
    private readonly ImmutableArray<Diagnostic> _previousDiagnostics;
    private readonly ImmutableArray<Metadata?> _previousReferenceMetadata;
    private readonly Predicate<MethodSymbol> _canReuse;

    internal CSharpMethodBodyReuse(
        CSharpCompilation previousCompilation,
        PEModuleBuilder previousModuleBuilder,
        ImmutableArray<Diagnostic> previousDiagnostics,
        Predicate<MethodSymbol> canReuse)
    {
        _previousCompilation = previousCompilation;
        _previousModuleBuilder = previousModuleBuilder;
        _previousDiagnostics = previousDiagnostics;
        _previousReferenceMetadata = CaptureReferenceMetadata(previousCompilation);
        _canReuse = canReuse;
    }

    internal CSharpMethodBodyReuse(
        CSharpCompilation previousCompilation,
        PEModuleBuilder previousModuleBuilder,
        ImmutableArray<Diagnostic> previousDiagnostics)
        : this(previousCompilation, previousModuleBuilder, previousDiagnostics, static _ => true)
    {
    }

    IMethodBodyReuseSession IMethodBodyReuse.CreateSession(CommonPEModuleBuilder moduleBuilder)
        => new Session(this, (PEModuleBuilder)moduleBuilder);

    private bool TryReuseMethodBody(
        MethodSymbol currentMethod,
        PEModuleBuilder currentModuleBuilder,
        MatcherState matcherState,
        DiagnosticBag diagnostics,
        out MethodBodyReuseGlobalFallbackReason? globalFallbackReason,
        out MethodBodyReuseBodyFallbackReason bodyFallbackReason)
    {
        globalFallbackReason = matcherState.GlobalFallbackReason;
        if (globalFallbackReason is not null)
        {
            bodyFallbackReason = default;
            return false;
        }

        Debug.Assert(matcherState.CurrentToPrevious is object);
        Debug.Assert(matcherState.PreviousToCurrent is object);

        if (matcherState.CurrentToPrevious.MapDefinition((Cci.IDefinition)currentMethod.GetCciAdapter())?.GetInternalSymbol() is not MethodSymbol previousMethod)
        {
            bodyFallbackReason = MethodBodyReuseBodyFallbackReason.PreviousSymbolUnavailable;
            return false;
        }

        if (_previousModuleBuilder.GetMethodBody(previousMethod) is not { } previousBody)
        {
            bodyFallbackReason = MethodBodyReuseBodyFallbackReason.PreviousBodyUnavailable;
            return false;
        }

        if (!HaveEquivalentSourceFiles(currentMethod, previousMethod))
        {
            bodyFallbackReason = MethodBodyReuseBodyFallbackReason.SourceChanged;
            return false;
        }

        if (GetUnsupportedBodyReason(previousBody) is { } unsupportedBodyReason)
        {
            bodyFallbackReason = unsupportedBodyReason;
            return false;
        }

        Cci.IImportScope? importScope = null;
        if (previousBody.ImportScope is ImportChain importChain)
        {
            if (!TryCloneEmptyImportChain(importChain, out var clonedImportChain))
            {
                bodyFallbackReason = MethodBodyReuseBodyFallbackReason.Imports;
                return false;
            }

            importScope = clonedImportChain.Translate(currentModuleBuilder, diagnostics);
        }
        else if (previousBody.ImportScope is object)
        {
            bodyFallbackReason = MethodBodyReuseBodyFallbackReason.Imports;
            return false;
        }

        if (!TryMapSequencePoints(previousBody.SequencePoints, currentModuleBuilder, out var sequencePoints))
        {
            bodyFallbackReason = MethodBodyReuseBodyFallbackReason.SequencePointDocument;
            return false;
        }

        if (!TryRewriteIL(previousBody.IL, currentModuleBuilder, matcherState.PreviousToCurrent, diagnostics, out var il, out bodyFallbackReason))
        {
            return false;
        }

        currentModuleBuilder.SetMethodBody(
            currentMethod,
            new ReusedMethodBody(previousBody, (Cci.IMethodDefinition)currentMethod.GetCciAdapter(), il, sequencePoints, importScope),
            reused: true);
        bodyFallbackReason = default;
        return true;
    }

    private MatcherState CreateMatcherState(PEModuleBuilder currentModuleBuilder)
    {
        var currentCompilation = currentModuleBuilder.Compilation;
        MethodBodyReuseGlobalFallbackReason? fallbackReason =
            ContainsWarningsOrErrors(_previousDiagnostics) ? MethodBodyReuseGlobalFallbackReason.PreviousDiagnostics :
            !HaveEquivalentCompilationOptions(currentCompilation) ? MethodBodyReuseGlobalFallbackReason.CompilationOptions :
            !string.Equals(_previousCompilation.AssemblyName, currentCompilation.AssemblyName, StringComparison.Ordinal) ? MethodBodyReuseGlobalFallbackReason.AssemblyIdentity :
            !HaveEquivalentReferences(currentCompilation) ? MethodBodyReuseGlobalFallbackReason.References :
            _previousModuleBuilder.DebugInformationFormat != currentModuleBuilder.DebugInformationFormat ||
                _previousModuleBuilder.EmittingPdb != currentModuleBuilder.EmittingPdb ||
                _previousModuleBuilder.DebugDocumentCount != currentModuleBuilder.DebugDocumentCount ? MethodBodyReuseGlobalFallbackReason.DebugInformation :
            !_previousModuleBuilder.EmitOptions.InstrumentationKinds.IsDefaultOrEmpty ||
                !currentModuleBuilder.EmitOptions.InstrumentationKinds.IsDefaultOrEmpty ? MethodBodyReuseGlobalFallbackReason.Instrumentation :
            GetDeclarationFallbackReason(currentCompilation) is { } declarationReason ? declarationReason :
            ContainsField(currentCompilation.SourceModule.GlobalNamespace) ? MethodBodyReuseGlobalFallbackReason.Fields :
            null;

        if (fallbackReason is { } reason)
        {
            return new MatcherState(reason);
        }

        return new MatcherState(
            CreateMatcher(currentCompilation, _previousCompilation),
            CreateMatcher(_previousCompilation, currentCompilation));
    }

    private static bool ContainsWarningsOrErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private bool HaveEquivalentCompilationOptions(CSharpCompilation currentCompilation)
    {
        var previousOptions = _previousCompilation.Options;
        var currentOptions = currentCompilation.Options;
        if (!HaveEquivalentSyntaxTreeOptionsProviders(
                previousOptions.SyntaxTreeOptionsProvider,
                currentOptions.SyntaxTreeOptionsProvider))
        {
            return false;
        }

        var normalizedCurrentOptions = currentCompilation.Options
            .WithCurrentLocalTime(previousOptions.CurrentLocalTime)
            .WithMetadataReferenceResolver(previousOptions.MetadataReferenceResolver)
            .WithSyntaxTreeOptionsProvider(previousOptions.SyntaxTreeOptionsProvider);

        return previousOptions.Equals(normalizedCurrentOptions);
    }

    private static bool HaveEquivalentSyntaxTreeOptionsProviders(
        SyntaxTreeOptionsProvider? previous,
        SyntaxTreeOptionsProvider? current)
    {
        if (ReferenceEquals(previous, current))
        {
            return true;
        }

        return previous is CompilerSyntaxTreeOptionsProvider previousCompilerProvider &&
            current is CompilerSyntaxTreeOptionsProvider currentCompilerProvider &&
            previousCompilerProvider.IsEquivalentTo(currentCompilerProvider);
    }

    private bool HaveEquivalentReferences(CSharpCompilation currentCompilation)
    {
        var previousReferences = _previousCompilation.ExternalReferences;
        var currentReferences = currentCompilation.ExternalReferences;
        if (previousReferences.Length != currentReferences.Length)
        {
            return false;
        }

        for (var i = 0; i < previousReferences.Length; i++)
        {
            if (previousReferences[i] is not PortableExecutableReference previousReference ||
                currentReferences[i] is not PortableExecutableReference currentReference)
            {
                if (!ReferenceEquals(previousReferences[i], currentReferences[i]))
                {
                    return false;
                }

                continue;
            }

            if (
                !previousReference.Properties.Equals(currentReference.Properties) ||
                !PathUtilities.Comparer.Equals(previousReference.FilePath, currentReference.FilePath) ||
                previousReference.Properties.Kind != MetadataImageKind.Assembly)
            {
                return false;
            }

            try
            {
                if (!ReferenceEquals(_previousReferenceMetadata[i], currentReference.GetMetadataNoCopy()))
                {
                    return false;
                }
            }
            catch (Exception e) when (e is IOException or BadImageFormatException)
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<Metadata?> CaptureReferenceMetadata(CSharpCompilation compilation)
    {
        var references = compilation.ExternalReferences;
        var builder = ImmutableArray.CreateBuilder<Metadata?>(references.Length);
        foreach (var reference in references)
        {
            if (reference is not PortableExecutableReference { Properties.Kind: MetadataImageKind.Assembly } peReference)
            {
                builder.Add(null);
                continue;
            }

            try
            {
                builder.Add(peReference.GetMetadataNoCopy());
            }
            catch (Exception e) when (e is IOException or BadImageFormatException)
            {
                builder.Add(null);
            }
        }

        return builder.MoveToImmutable();
    }

    private MethodBodyReuseGlobalFallbackReason? GetDeclarationFallbackReason(CSharpCompilation currentCompilation)
    {
        var previousTrees = _previousCompilation.SyntaxTrees;
        var currentTrees = currentCompilation.SyntaxTrees;
        if (previousTrees.Length != currentTrees.Length)
        {
            return MethodBodyReuseGlobalFallbackReason.Declarations;
        }

        for (var i = 0; i < currentTrees.Length; i++)
        {
            if (!previousTrees[i].Options.Equals(currentTrees[i].Options))
            {
                return MethodBodyReuseGlobalFallbackReason.ParseOptions;
            }

            if (!string.Equals(previousTrees[i].FilePath, currentTrees[i].FilePath, StringComparison.Ordinal) ||
                !previousTrees[i].IsEquivalentTo(currentTrees[i], topLevel: true))
            {
                return MethodBodyReuseGlobalFallbackReason.Declarations;
            }
        }

        return null;
    }

    private static bool HaveEquivalentSourceFiles(MethodSymbol currentMethod, MethodSymbol previousMethod)
    {
        var currentReferences = currentMethod.DeclaringSyntaxReferences;
        var previousReferences = previousMethod.DeclaringSyntaxReferences;
        if (currentReferences.Length != previousReferences.Length)
        {
            return false;
        }

        foreach (var currentReference in currentReferences)
        {
            var currentTree = currentReference.SyntaxTree;
            SyntaxTree? previousTree = null;
            foreach (var previousReference in previousReferences)
            {
                if (string.Equals(currentTree.FilePath, previousReference.SyntaxTree.FilePath, StringComparison.Ordinal))
                {
                    previousTree = previousReference.SyntaxTree;
                    break;
                }
            }

            if (previousTree is null ||
                !currentTree.GetText().ContentEquals(previousTree.GetText()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsField(NamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetMembersUnordered())
        {
            if (member is NamespaceSymbol childNamespace && ContainsField(childNamespace) ||
                member is NamedTypeSymbol type && ContainsField(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsField(NamedTypeSymbol type)
    {
        foreach (var member in type.GetMembersUnordered())
        {
            if (member is FieldSymbol ||
                member is NamedTypeSymbol nestedType && ContainsField(nestedType))
            {
                return true;
            }
        }

        return false;
    }

    private static CSharpSymbolMatcher CreateMatcher(CSharpCompilation from, CSharpCompilation to)
        => new(
            from.SourceAssembly,
            to.SourceAssembly,
            SynthesizedTypeMaps.Empty,
            otherSynthesizedMembers: SpecializedCollections.EmptyReadOnlyDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>>(),
            otherDeletedMembers: SpecializedCollections.EmptyReadOnlyDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>>());

    private static MethodBodyReuseBodyFallbackReason? GetUnsupportedBodyReason(Cci.IMethodBody body)
    {
        if (!body.ExceptionRegions.IsDefaultOrEmpty)
        {
            return MethodBodyReuseBodyFallbackReason.ExceptionHandling;
        }

        if (!body.LocalVariables.IsDefaultOrEmpty || !body.LocalScopes.IsDefaultOrEmpty)
        {
            return MethodBodyReuseBodyFallbackReason.Locals;
        }

        if (body.MoveNextBodyInfo is object ||
            body.StateMachineTypeName is object ||
            !body.StateMachineHoistedLocalScopes.IsDefaultOrEmpty ||
            !body.StateMachineHoistedLocalSlots.IsDefaultOrEmpty ||
            !body.StateMachineAwaiterSlots.IsDefaultOrEmpty ||
            !body.StateMachineStatesDebugInfo.States.IsDefaultOrEmpty ||
            body.StateMachineStatesDebugInfo.FirstUnusedIncreasingStateMachineState is object ||
            body.StateMachineStatesDebugInfo.FirstUnusedDecreasingStateMachineState is object)
        {
            return MethodBodyReuseBodyFallbackReason.StateMachine;
        }

        if (!body.ClosureDebugInfo.IsDefaultOrEmpty ||
            !body.LambdaDebugInfo.IsDefaultOrEmpty ||
            !body.OrderedLambdaRuntimeRudeEdits.IsDefaultOrEmpty)
        {
            return MethodBodyReuseBodyFallbackReason.SynthesizedDebugInformation;
        }

        if (body.HasDynamicLocalVariables)
        {
            return MethodBodyReuseBodyFallbackReason.Dynamic;
        }

        if (body.HasStackalloc)
        {
            return MethodBodyReuseBodyFallbackReason.StackAlloc;
        }

        if (!body.CodeCoverageSpans.IsDefaultOrEmpty)
        {
            return MethodBodyReuseBodyFallbackReason.CodeCoverage;
        }

        return null;
    }

    private static bool TryCloneEmptyImportChain(ImportChain importChain, out ImportChain clonedImportChain)
    {
        ImportChain? result = null;
        var depth = 0;
        for (var current = importChain; current is object; current = current.ParentOpt)
        {
            if (!current.Imports.UsingAliases.IsEmpty ||
                !current.Imports.Usings.IsEmpty ||
                !current.Imports.ExternAliases.IsEmpty)
            {
                clonedImportChain = null!;
                return false;
            }

            depth++;
        }

        while (depth-- > 0)
        {
            result = new ImportChain(Imports.Empty, result);
        }

        clonedImportChain = result!;
        return true;
    }

    private static bool TryMapSequencePoints(
        ImmutableArray<Cci.SequencePoint> previousSequencePoints,
        PEModuleBuilder currentModuleBuilder,
        out ImmutableArray<Cci.SequencePoint> sequencePoints)
    {
        if (previousSequencePoints.IsEmpty)
        {
            sequencePoints = previousSequencePoints;
            return true;
        }

        var builder = ImmutableArray.CreateBuilder<Cci.SequencePoint>(previousSequencePoints.Length);
        foreach (var sequencePoint in previousSequencePoints)
        {
            var document = currentModuleBuilder.DebugDocumentsBuilder.TryGetDebugDocumentForNormalizedPath(sequencePoint.Document.Location);
            if (document is null)
            {
                sequencePoints = default;
                return false;
            }

            builder.Add(new Cci.SequencePoint(
                document,
                sequencePoint.Offset,
                sequencePoint.StartLine,
                sequencePoint.StartColumn,
                sequencePoint.EndLine,
                sequencePoint.EndColumn));
        }

        sequencePoints = builder.MoveToImmutable();
        return true;
    }

    private bool TryRewriteIL(
        ImmutableArray<byte> previousIL,
        PEModuleBuilder currentModuleBuilder,
        CSharpSymbolMatcher previousToCurrent,
        DiagnosticBag diagnostics,
        out ImmutableArray<byte> il,
        out MethodBodyReuseBodyFallbackReason fallbackReason)
    {
        if (!CanRewriteIL(previousIL, previousToCurrent, out fallbackReason))
        {
            il = default;
            return false;
        }

        var builder = previousIL.ToBuilder();
        var offset = 0;

        while (offset < previousIL.Length)
        {
            var operandType = Cci.InstructionOperandTypes.ReadOperandType(previousIL, ref offset);
            switch (operandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    {
                        var previousToken = ReadInt32(previousIL, offset);
                        if (_previousModuleBuilder.GetReferenceFromToken((uint)previousToken) is not Cci.IReference previousReference ||
                            previousToCurrent.MapReference(previousReference) is not { } currentReference)
                        {
                            il = default;
                            fallbackReason = MethodBodyReuseBodyFallbackReason.ReferenceMapping;
                            return false;
                        }

                        var currentToken = currentModuleBuilder.GetFakeSymbolTokenForIL(currentReference, syntaxNode: null, diagnostics);
                        WriteInt32(builder, offset, (int)currentToken);
                        offset += 4;
                        break;
                    }

                case OperandType.InlineSig:
                    il = default;
                    fallbackReason = MethodBodyReuseBodyFallbackReason.InlineSignature;
                    return false;

                case OperandType.InlineString:
                    {
                        var previousToken = ReadInt32(previousIL, offset);
                        if ((uint)previousToken == Cci.MetadataWriter.ModuleVersionIdStringToken)
                        {
                            offset += 4;
                            break;
                        }

                        if (!currentModuleBuilder.TryGetFakeStringTokenForIL(
                                _previousModuleBuilder.GetStringFromToken((uint)previousToken),
                                out var currentToken))
                        {
                            il = default;
                            fallbackReason = MethodBodyReuseBodyFallbackReason.StringToken;
                            return false;
                        }

                        WriteInt32(builder, offset, (int)currentToken);
                        offset += 4;
                        break;
                    }

                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                    offset += 4;
                    break;

                case OperandType.InlineSwitch:
                    offset += (ReadInt32(previousIL, offset) + 1) * 4;
                    break;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    offset += 8;
                    break;

                case OperandType.InlineNone:
                    break;

                case OperandType.InlineVar:
                    offset += 2;
                    break;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    offset += 1;
                    break;

                default:
                    il = default;
                    fallbackReason = MethodBodyReuseBodyFallbackReason.UnsupportedInstruction;
                    return false;
            }
        }

        il = builder.MoveToImmutable();
        fallbackReason = default;
        return true;
    }

    private bool CanRewriteIL(
        ImmutableArray<byte> previousIL,
        CSharpSymbolMatcher previousToCurrent,
        out MethodBodyReuseBodyFallbackReason fallbackReason)
    {
        var offset = 0;
        while (offset < previousIL.Length)
        {
            var operandType = Cci.InstructionOperandTypes.ReadOperandType(previousIL, ref offset);
            switch (operandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    {
                        var previousToken = ReadInt32(previousIL, offset);
                        if ((previousToken & unchecked((int)0xff000000)) != 0 ||
                            _previousModuleBuilder.GetReferenceFromToken((uint)previousToken) is not Cci.IReference previousReference ||
                            previousToCurrent.MapReference(previousReference) is null)
                        {
                            fallbackReason = MethodBodyReuseBodyFallbackReason.ReferenceMapping;
                            return false;
                        }

                        offset += 4;
                        break;
                    }

                case OperandType.InlineSig:
                    fallbackReason = MethodBodyReuseBodyFallbackReason.InlineSignature;
                    return false;

                case OperandType.InlineString:
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                    offset += 4;
                    break;

                case OperandType.InlineSwitch:
                    offset += (ReadInt32(previousIL, offset) + 1) * 4;
                    break;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    offset += 8;
                    break;

                case OperandType.InlineNone:
                    break;

                case OperandType.InlineVar:
                    offset += 2;
                    break;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    offset += 1;
                    break;

                default:
                    fallbackReason = MethodBodyReuseBodyFallbackReason.UnsupportedInstruction;
                    return false;
            }
        }

        fallbackReason = default;
        return true;
    }

    private sealed class Session : IMethodBodyReuseSession
    {
        private readonly CSharpMethodBodyReuse _reuse;
        private readonly PEModuleBuilder _moduleBuilder;
        private readonly Lazy<MatcherState> _lazyMatcherState;
        private readonly MethodBodyReuseStatisticsCollector _statistics = new();

        internal Session(CSharpMethodBodyReuse reuse, PEModuleBuilder moduleBuilder)
        {
            _reuse = reuse;
            _moduleBuilder = moduleBuilder;
            _lazyMatcherState = new Lazy<MatcherState>(() => reuse.CreateMatcherState(moduleBuilder));
        }

        bool IMethodBodyReuseSession.ShouldCompile(ISymbolInternal symbol)
            => symbol is not MethodSymbol method ||
               method.MethodKind != MethodKind.Ordinary ||
               !_reuse._canReuse(method);

        bool IMethodBodyReuseSession.TryReuseMethodBody(
            IMethodSymbolInternal method,
            CommonPEModuleBuilder moduleBuilder,
            DiagnosticBag diagnostics)
        {
            Debug.Assert(ReferenceEquals(moduleBuilder, _moduleBuilder));
            _statistics.RecordReuseAttempt();

            if (_reuse.TryReuseMethodBody(
                    (MethodSymbol)method,
                    _moduleBuilder,
                    _lazyMatcherState.Value,
                    diagnostics,
                    out var globalFallbackReason,
                    out var bodyFallbackReason))
            {
                return true;
            }

            if (globalFallbackReason is { } globalReason)
            {
                _statistics.RecordFallback(globalReason);
            }
            else
            {
                _statistics.RecordFallback(bodyFallbackReason);
            }

            return false;
        }

        void IMethodBodyReuseSession.RecordEmittedBody(bool reused)
        {
            if (reused)
            {
                _statistics.RecordReusedBody();
            }
            else
            {
                _statistics.RecordCompiledBody();
            }
        }

        MethodBodyReuseStatistics IMethodBodyReuseSession.Complete(bool succeeded)
            => _statistics.Complete(succeeded);
    }

    private sealed class MatcherState
    {
        internal MethodBodyReuseGlobalFallbackReason? GlobalFallbackReason { get; }
        internal CSharpSymbolMatcher? CurrentToPrevious { get; }
        internal CSharpSymbolMatcher? PreviousToCurrent { get; }

        internal MatcherState(
            CSharpSymbolMatcher currentToPrevious,
            CSharpSymbolMatcher previousToCurrent)
        {
            CurrentToPrevious = currentToPrevious;
            PreviousToCurrent = previousToCurrent;
        }

        internal MatcherState(MethodBodyReuseGlobalFallbackReason globalFallbackReason)
        {
            GlobalFallbackReason = globalFallbackReason;
        }
    }

    private static int ReadInt32(ImmutableArray<byte> bytes, int offset)
        => bytes[offset] |
           bytes[offset + 1] << 8 |
           bytes[offset + 2] << 16 |
           bytes[offset + 3] << 24;

    private static void WriteInt32(ImmutableArray<byte>.Builder bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private sealed class ReusedMethodBody : Cci.IMethodBody
    {
        public ImmutableArray<Cci.ExceptionHandlerRegion> ExceptionRegions { get; }
        public bool AreLocalsZeroed { get; }
        public bool HasStackalloc { get; }
        public ImmutableArray<Cci.ILocalDefinition> LocalVariables { get; }
        public Cci.IMethodDefinition MethodDefinition { get; }
        public StateMachineMoveNextBodyDebugInfo? MoveNextBodyInfo { get; }
        public ushort MaxStack { get; }
        public ImmutableArray<byte> IL { get; }
        public ImmutableArray<Cci.SequencePoint> SequencePoints { get; }
        public bool HasDynamicLocalVariables { get; }
        public ImmutableArray<Cci.LocalScope> LocalScopes { get; }
        public Cci.IImportScope? ImportScope { get; }
        public DebugId MethodId { get; }
        public ImmutableArray<StateMachineHoistedLocalScope> StateMachineHoistedLocalScopes { get; }
        public string? StateMachineTypeName { get; }
        public ImmutableArray<EncHoistedLocalInfo> StateMachineHoistedLocalSlots { get; }
        public ImmutableArray<Cci.ITypeReference?> StateMachineAwaiterSlots { get; }
        public ImmutableArray<EncClosureInfo> ClosureDebugInfo { get; }
        public ImmutableArray<EncLambdaInfo> LambdaDebugInfo { get; }
        public ImmutableArray<LambdaRuntimeRudeEditInfo> OrderedLambdaRuntimeRudeEdits { get; }
        public StateMachineStatesDebugInfo StateMachineStatesDebugInfo { get; }
        public ImmutableArray<SourceSpan> CodeCoverageSpans { get; }
        public bool IsPrimaryConstructor { get; }

        internal ReusedMethodBody(
            Cci.IMethodBody previousBody,
            Cci.IMethodDefinition methodDefinition,
            ImmutableArray<byte> il,
            ImmutableArray<Cci.SequencePoint> sequencePoints,
            Cci.IImportScope? importScope)
        {
            ExceptionRegions = previousBody.ExceptionRegions;
            AreLocalsZeroed = previousBody.AreLocalsZeroed;
            HasStackalloc = previousBody.HasStackalloc;
            LocalVariables = previousBody.LocalVariables;
            MethodDefinition = methodDefinition;
            MoveNextBodyInfo = previousBody.MoveNextBodyInfo;
            MaxStack = previousBody.MaxStack;
            IL = il;
            SequencePoints = sequencePoints;
            HasDynamicLocalVariables = previousBody.HasDynamicLocalVariables;
            LocalScopes = previousBody.LocalScopes;
            ImportScope = importScope;
            MethodId = previousBody.MethodId;
            StateMachineHoistedLocalScopes = previousBody.StateMachineHoistedLocalScopes;
            StateMachineTypeName = previousBody.StateMachineTypeName;
            StateMachineHoistedLocalSlots = previousBody.StateMachineHoistedLocalSlots;
            StateMachineAwaiterSlots = previousBody.StateMachineAwaiterSlots;
            ClosureDebugInfo = previousBody.ClosureDebugInfo;
            LambdaDebugInfo = previousBody.LambdaDebugInfo;
            OrderedLambdaRuntimeRudeEdits = previousBody.OrderedLambdaRuntimeRudeEdits;
            StateMachineStatesDebugInfo = previousBody.StateMachineStatesDebugInfo;
            CodeCoverageSpans = previousBody.CodeCoverageSpans;
            IsPrimaryConstructor = previousBody.IsPrimaryConstructor;
        }
    }
}
