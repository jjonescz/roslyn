// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Cci;
using Microsoft.CodeAnalysis.CodeGen;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols;

internal sealed class SynthesizedDataStringHolderType : NamedTypeSymbol
{
    private readonly DataStringHolder _dataStringHolder;
    private readonly NamespaceSymbol _globalNamespace;
    private readonly NamedTypeSymbol _objectType;
    private readonly FieldSymbol _stringField;
    private readonly MethodSymbol _staticConstructor;

    public SynthesizedDataStringHolderType(
        DataStringHolder dataStringHolder,
        NamespaceSymbol globalNamespace,
        NamedTypeSymbol objectType,
        NamedTypeSymbol stringType)
    {
        _dataStringHolder = dataStringHolder;
        _globalNamespace = globalNamespace;
        _objectType = objectType;
        _stringField = new SynthesizedFieldSymbol(
            containingType: this,
            type: stringType,
            name: "s",
            isReadOnly: true,
            isStatic: true);
        _staticConstructor = new StaticConstructor(this);
    }

    public override int Arity => 0;

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => [];

    public override NamedTypeSymbol ConstructedFrom => this;

    public override bool MightContainExtensionMethods => false;

    public override string Name => _dataStringHolder.Name;

    public override IEnumerable<string> MemberNames => [];

    public override Accessibility DeclaredAccessibility => Accessibility.Internal;

    public override bool IsSerializable => false;

    public override bool AreLocalsZeroed => false;

    public override TypeKind TypeKind => TypeKind.Class;

    public override bool IsRefLikeType => false;

    public override bool IsReadOnly => false;

    public override Symbol ContainingSymbol => _globalNamespace;

    public override ImmutableArray<Location> Locations => [];

    public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => [];

    public override bool IsStatic => true;

    public override bool IsAbstract => false;

    public override bool IsSealed => false;

    internal override ImmutableArray<TypeWithAnnotations> TypeArgumentsWithAnnotationsNoUseSiteDiagnostics => [];

    internal override bool IsFileLocal => false;

    internal override FileIdentifier? AssociatedFileIdentifier => null;

    internal override bool MangleName => false;

    internal override bool HasDeclaredRequiredMembers => false;

    internal override bool HasCodeAnalysisEmbeddedAttribute => false;

    internal override bool IsInterpolatedStringHandlerType => false;

    internal override bool HasSpecialName => false;

    internal override bool IsComImport => false;

    internal override bool IsWindowsRuntimeImport => false;

    internal override bool ShouldAddWinRTMembers => false;

    internal override TypeLayout Layout => default;

    internal override CharSet MarshallingCharSet => DefaultMarshallingCharSet;

    internal override bool HasDeclarativeSecurity => false;

    internal override bool IsInterface => false;

    internal override NamedTypeSymbol? NativeIntegerUnderlyingType => null;

    internal override NamedTypeSymbol BaseTypeNoUseSiteDiagnostics => _objectType;

    internal override bool IsRecord => false;

    internal override bool IsRecordStruct => false;

    internal override ObsoleteAttributeData? ObsoleteAttributeData => null;

    public override ImmutableArray<Symbol> GetMembers()
    {
        return [_stringField, _staticConstructor];
    }

    public override ImmutableArray<Symbol> GetMembers(string name)
    {
        return GetMembers().WhereAsArray(m => m.Name == name);
    }

    public override ImmutableArray<NamedTypeSymbol> GetTypeMembers() => [];

    public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(ReadOnlyMemory<char> name, int arity) => [];

    public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(ReadOnlyMemory<char> name) => [];

    protected override NamedTypeSymbol WithTupleDataCore(TupleExtraData newData) => throw ExceptionUtilities.Unreachable();

    internal override NamedTypeSymbol AsNativeInteger() => throw ExceptionUtilities.Unreachable();

    internal override ImmutableArray<string> GetAppliedConditionalSymbols() => [];

    internal override AttributeUsageInfo GetAttributeUsageInfo() => default;

    internal override NamedTypeSymbol GetDeclaredBaseType(ConsList<TypeSymbol> basesBeingResolved) => BaseTypeNoUseSiteDiagnostics;

    internal override ImmutableArray<NamedTypeSymbol> GetDeclaredInterfaces(ConsList<TypeSymbol> basesBeingResolved) => [];

    internal override ImmutableArray<Symbol> GetEarlyAttributeDecodingMembers() => throw ExceptionUtilities.Unreachable();

    internal override ImmutableArray<Symbol> GetEarlyAttributeDecodingMembers(string name) => throw ExceptionUtilities.Unreachable();

    internal override IEnumerable<FieldSymbol> GetFieldsToEmit() => [_stringField];

    internal override bool GetGuidString(out string? guidString)
    {
        guidString = null;
        return false;
    }

    internal override ImmutableArray<NamedTypeSymbol> GetInterfacesToEmit() => [];

    internal override IEnumerable<Cci.SecurityAttribute> GetSecurityInformation() => [];

    internal override bool HasAsyncMethodBuilderAttribute(out TypeSymbol? builderArgument)
    {
        builderArgument = null;
        return false;
    }

    internal override bool HasCollectionBuilderAttribute(out TypeSymbol? builderType, out string? methodName)
    {
        builderType = null;
        methodName = null;
        return false;
    }

    internal override bool HasInlineArrayAttribute(out int length)
    {
        length = default;
        return false;
    }

    internal override bool HasPossibleWellKnownCloneMethod() => false;

    internal override ImmutableArray<NamedTypeSymbol> InterfacesNoUseSiteDiagnostics(ConsList<TypeSymbol>? basesBeingResolved = null) => [];

    internal override IEnumerable<(MethodSymbol Body, MethodSymbol Implemented)> SynthesizedInterfaceMethodImpls() => [];

    private sealed class StaticConstructor(
        SynthesizedDataStringHolderType dataStringHolder)
        : MethodSymbol
    {
        private readonly SynthesizedDataStringHolderType _dataStringHolder = dataStringHolder;

        public override MethodKind MethodKind => MethodKind.StaticConstructor;

        public override int Arity => 0;

        public override bool IsExtensionMethod => false;

        public override bool HidesBaseMethodsByName => false;

        public override bool IsVararg => false;

        public override bool ReturnsVoid => true;

        public override bool IsAsync => false;

        public override RefKind RefKind => RefKind.None;

        public override TypeWithAnnotations ReturnTypeWithAnnotations => TypeWithAnnotations.Create(ContainingAssembly.GetSpecialType(SpecialType.System_Void));

        public override FlowAnalysisAnnotations ReturnTypeFlowAnalysisAnnotations => FlowAnalysisAnnotations.None;

        public override ImmutableHashSet<string> ReturnNotNullIfParameterNotNull => [];

        public override FlowAnalysisAnnotations FlowAnalysisAnnotations => FlowAnalysisAnnotations.None;

        public override ImmutableArray<TypeWithAnnotations> TypeArgumentsWithAnnotations => [];

        public override ImmutableArray<TypeParameterSymbol> TypeParameters => [];

        public override ImmutableArray<ParameterSymbol> Parameters => [];

        public override ImmutableArray<MethodSymbol> ExplicitInterfaceImplementations => [];

        public override ImmutableArray<CustomModifier> RefCustomModifiers => [];

        public override Symbol? AssociatedSymbol => null;

        public override bool AreLocalsZeroed => false;

        public override Symbol ContainingSymbol => _dataStringHolder;

        public override ImmutableArray<Location> Locations => [];

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => [];

        public override Accessibility DeclaredAccessibility => Accessibility.Private;

        public override bool IsStatic => true;

        public override bool IsVirtual => false;

        public override bool IsOverride => false;

        public override bool IsAbstract => false;

        public override bool IsSealed => false;

        public override bool IsExtern => false;

        protected override bool HasSetsRequiredMembersImpl => false;

        internal override bool HasSpecialName => true;

        internal override MethodImplAttributes ImplementationAttributes => default;

        internal override bool HasDeclarativeSecurity => false;

        internal override MarshalPseudoCustomAttributeData? ReturnValueMarshallingInformation => null;

        internal override bool RequiresSecurityObject => false;

        internal override bool IsDeclaredReadOnly => false;

        internal override bool IsInitOnly => false;

        internal override bool HasUnscopedRefAttribute => false;

        internal override bool UseUpdatedEscapeRules => false;

        internal override Cci.CallingConvention CallingConvention => Cci.CallingConvention.Default;

        internal override bool GenerateDebugInfo => false;

        internal override ObsoleteAttributeData? ObsoleteAttributeData => null;

        public override DllImportData? GetDllImportData() => null;

        internal override int CalculateLocalSyntaxOffset(int localPosition, SyntaxTree localTree) => throw ExceptionUtilities.Unreachable();

        internal override ImmutableArray<string> GetAppliedConditionalSymbols() => [];

        internal override IEnumerable<SecurityAttribute> GetSecurityInformation() => [];

        internal override UnmanagedCallersOnlyAttributeData? GetUnmanagedCallersOnlyAttributeData(bool forceComplete) => null;

        internal override bool HasAsyncMethodBuilderAttribute(out TypeSymbol? builderArgument)
        {
            builderArgument = null;
            return false;
        }

        internal override bool IsMetadataNewSlot(bool ignoreInterfaceImplementationChanges = false) => false;

        internal override bool IsMetadataVirtual(IsMetadataVirtualOption option = IsMetadataVirtualOption.None) => false;

        internal override bool IsNullableAnalysisEnabled() => false;

        internal override int? TryGetOverloadResolutionPriority() => null;

        internal override bool SynthesizesLoweredBoundBody => true;

        internal override void GenerateMethodBody(TypeCompilationState compilationState, BindingDiagnosticBag diagnostics)
        {
            base.GenerateMethodBody(compilationState, diagnostics);
        }
    }
}
