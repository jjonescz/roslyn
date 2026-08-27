// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.Debugging;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Symbols;

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
    private readonly Predicate<MethodSymbol> _canReuse;
    private readonly ConditionalWeakTable<PEModuleBuilder, MatcherState> _matcherStates = new();
    private int _reusedMethodCount;

    internal CSharpMethodBodyReuse(
        CSharpCompilation previousCompilation,
        PEModuleBuilder previousModuleBuilder,
        ImmutableArray<Diagnostic> previousDiagnostics,
        Predicate<MethodSymbol> canReuse)
    {
        _previousCompilation = previousCompilation;
        _previousModuleBuilder = previousModuleBuilder;
        _previousDiagnostics = previousDiagnostics;
        _canReuse = canReuse;
    }

    internal int ReusedMethodCount => Volatile.Read(ref _reusedMethodCount);

    bool IMethodBodyReuse.ShouldCompile(ISymbolInternal symbol)
        => symbol is not MethodSymbol method ||
           method.MethodKind != MethodKind.Ordinary ||
           !_canReuse(method);

    bool IMethodBodyReuse.TryReuseMethodBody(
        IMethodSymbolInternal method,
        CommonPEModuleBuilder moduleBuilder,
        DiagnosticBag diagnostics)
    {
        var currentMethod = (MethodSymbol)method;
        var currentModuleBuilder = (PEModuleBuilder)moduleBuilder;

        var matcherState = _matcherStates.GetValue(currentModuleBuilder, CreateMatcherState);
        if (!matcherState.IsCompatible)
        {
            return false;
        }

        Debug.Assert(matcherState.CurrentToPrevious is object);
        Debug.Assert(matcherState.PreviousToCurrent is object);

        if (matcherState.CurrentToPrevious.MapDefinition((Cci.IDefinition)currentMethod.GetCciAdapter())?.GetInternalSymbol() is not MethodSymbol previousMethod ||
            _previousModuleBuilder.GetMethodBody(previousMethod) is not { } previousBody ||
            !HaveEquivalentSourceFiles(currentMethod, previousMethod) ||
            !IsSupported(previousBody))
        {
            return false;
        }

        Cci.IImportScope? importScope = null;
        if (previousBody.ImportScope is ImportChain importChain)
        {
            if (!TryCloneEmptyImportChain(importChain, out var clonedImportChain))
            {
                return false;
            }

            importScope = clonedImportChain.Translate(currentModuleBuilder, diagnostics);
        }
        else if (previousBody.ImportScope is object)
        {
            return false;
        }

        if (!TryMapSequencePoints(previousBody.SequencePoints, currentModuleBuilder, out var sequencePoints) ||
            !TryRewriteIL(previousBody.IL, currentModuleBuilder, matcherState.PreviousToCurrent, diagnostics, out var il))
        {
            return false;
        }

        currentModuleBuilder.SetMethodBody(
            currentMethod,
            new ReusedMethodBody(previousBody, (Cci.IMethodDefinition)currentMethod.GetCciAdapter(), il, sequencePoints, importScope));
        Interlocked.Increment(ref _reusedMethodCount);
        return true;
    }

    private MatcherState CreateMatcherState(PEModuleBuilder currentModuleBuilder)
    {
        var currentCompilation = currentModuleBuilder.Compilation;
        if (!_previousDiagnostics.IsEmpty ||
            !_previousCompilation.Options.Equals(currentCompilation.Options) ||
            !string.Equals(_previousCompilation.AssemblyName, currentCompilation.AssemblyName, StringComparison.Ordinal) ||
            !ReferenceEquals(_previousCompilation.GetBoundReferenceManager(), currentCompilation.GetBoundReferenceManager()) ||
            _previousModuleBuilder.DebugInformationFormat != currentModuleBuilder.DebugInformationFormat ||
            _previousModuleBuilder.EmittingPdb != currentModuleBuilder.EmittingPdb ||
            _previousModuleBuilder.DebugDocumentCount != currentModuleBuilder.DebugDocumentCount ||
            !_previousModuleBuilder.EmitOptions.InstrumentationKinds.IsDefaultOrEmpty ||
            !currentModuleBuilder.EmitOptions.InstrumentationKinds.IsDefaultOrEmpty ||
            !HaveEquivalentDeclarations(currentCompilation) ||
            ContainsField(currentCompilation.SourceModule.GlobalNamespace))
        {
            return MatcherState.Incompatible;
        }

        return new MatcherState(
            CreateMatcher(currentCompilation, _previousCompilation),
            CreateMatcher(_previousCompilation, currentCompilation));
    }

    private bool HaveEquivalentDeclarations(CSharpCompilation currentCompilation)
    {
        var previousTrees = _previousCompilation.SyntaxTrees;
        var currentTrees = currentCompilation.SyntaxTrees;
        if (previousTrees.Length != currentTrees.Length)
        {
            return false;
        }

        for (var i = 0; i < currentTrees.Length; i++)
        {
            if (!string.Equals(previousTrees[i].FilePath, currentTrees[i].FilePath, StringComparison.Ordinal) ||
                !previousTrees[i].Options.Equals(currentTrees[i].Options) ||
                !previousTrees[i].IsEquivalentTo(currentTrees[i], topLevel: true))
            {
                return false;
            }
        }

        return true;
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

    private static bool IsSupported(Cci.IMethodBody body)
        => body.LocalVariables.IsDefaultOrEmpty &&
           body.ExceptionRegions.IsDefaultOrEmpty &&
           body.LocalScopes.IsDefaultOrEmpty &&
           body.MoveNextBodyInfo is null &&
           body.StateMachineTypeName is null &&
           body.StateMachineHoistedLocalScopes.IsDefaultOrEmpty &&
           body.StateMachineHoistedLocalSlots.IsDefaultOrEmpty &&
           body.StateMachineAwaiterSlots.IsDefaultOrEmpty &&
           body.ClosureDebugInfo.IsDefaultOrEmpty &&
           body.LambdaDebugInfo.IsDefaultOrEmpty &&
           body.OrderedLambdaRuntimeRudeEdits.IsDefaultOrEmpty &&
           body.StateMachineStatesDebugInfo.States.IsDefaultOrEmpty &&
           body.StateMachineStatesDebugInfo.FirstUnusedIncreasingStateMachineState is null &&
           body.StateMachineStatesDebugInfo.FirstUnusedDecreasingStateMachineState is null &&
           body.CodeCoverageSpans.IsDefaultOrEmpty &&
           !body.HasDynamicLocalVariables &&
           !body.HasStackalloc;

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
        out ImmutableArray<byte> il)
    {
        if (!CanRewriteIL(previousIL, previousToCurrent))
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
                            return false;
                        }

                        var currentToken = currentModuleBuilder.GetFakeSymbolTokenForIL(currentReference, syntaxNode: null, diagnostics);
                        WriteInt32(builder, offset, (int)currentToken);
                        offset += 4;
                        break;
                    }

                case OperandType.InlineSig:
                    il = default;
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
                    return false;
            }
        }

        il = builder.MoveToImmutable();
        return true;
    }

    private bool CanRewriteIL(ImmutableArray<byte> previousIL, CSharpSymbolMatcher previousToCurrent)
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
                            return false;
                        }

                        offset += 4;
                        break;
                    }

                case OperandType.InlineSig:
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
                    return false;
            }
        }

        return true;
    }

    private sealed class MatcherState(
        CSharpSymbolMatcher? currentToPrevious,
        CSharpSymbolMatcher? previousToCurrent)
    {
        internal static readonly MatcherState Incompatible = new(null, null);

        internal bool IsCompatible => CurrentToPrevious is object;
        internal CSharpSymbolMatcher? CurrentToPrevious { get; } = currentToPrevious;
        internal CSharpSymbolMatcher? PreviousToCurrent { get; } = previousToCurrent;
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
