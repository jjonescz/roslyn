' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Reflection.Metadata
Imports Microsoft.CodeAnalysis.Collections
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols

Namespace Microsoft.CodeAnalysis.VisualBasic.Emit

    ' Pointer types can occur in synthesized method signatures.
    Friend NotInheritable Class PointerTypeReference
        Implements Cci.IPointerTypeReference

        Private ReadOnly _pointedAtType As TypeSymbol

        Public Sub New(pointedAtType As TypeSymbol)
            _pointedAtType = pointedAtType
        End Sub

        Private Function IPointerTypeReferenceGetTargetType(context As EmitContext) As Cci.ITypeReference Implements Cci.IPointerTypeReference.GetTargetType
            Dim moduleBeingBuilt = DirectCast(context.Module, PEModuleBuilder)
            Return moduleBeingBuilt.Translate(_pointedAtType, syntaxNodeOpt:=DirectCast(context.SyntaxNode, VisualBasicSyntaxNode), diagnostics:=context.Diagnostics)
        End Function

        Private ReadOnly Property ITypeReferenceIsEnum As Boolean Implements Cci.ITypeReference.IsEnum
            Get
                Return False
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceIsValueType As Boolean Implements Cci.ITypeReference.IsValueType
            Get
                Return False
            End Get
        End Property

        Private Function ITypeReferenceGetResolvedType(context As EmitContext) As Cci.ITypeDefinition Implements Cci.ITypeReference.GetResolvedType
            Return Nothing
        End Function

        Private ReadOnly Property ITypeReferenceTypeCode As Cci.PrimitiveTypeCode Implements Cci.ITypeReference.TypeCode
            Get
                Return Cci.PrimitiveTypeCode.Pointer
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceTypeDef As TypeDefinitionHandle Implements Cci.ITypeReference.TypeDef
            Get
                Return Nothing
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceAsGenericMethodParameterReference As Cci.IGenericMethodParameterReference Implements Cci.ITypeReference.AsGenericMethodParameterReference
            Get
                Return Nothing
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceAsGenericTypeInstanceReference As Cci.IGenericTypeInstanceReference Implements Cci.ITypeReference.AsGenericTypeInstanceReference
            Get
                Return Nothing
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceAsGenericTypeParameterReference As Cci.IGenericTypeParameterReference Implements Cci.ITypeReference.AsGenericTypeParameterReference
            Get
                Return Nothing
            End Get
        End Property

        Private Function ITypeReferenceAsNamespaceTypeDefinition(context As EmitContext) As Cci.INamespaceTypeDefinition Implements Cci.ITypeReference.AsNamespaceTypeDefinition
            Return Nothing
        End Function

        Private ReadOnly Property ITypeReferenceAsNamespaceTypeReference As Cci.INamespaceTypeReference Implements Cci.ITypeReference.AsNamespaceTypeReference
            Get
                Return Nothing
            End Get
        End Property

        Private Function ITypeReferenceAsNestedTypeDefinition(context As EmitContext) As Cci.INestedTypeDefinition Implements Cci.ITypeReference.AsNestedTypeDefinition
            Return Nothing
        End Function

        Private ReadOnly Property ITypeReferenceAsNestedTypeReference As Cci.INestedTypeReference Implements Cci.ITypeReference.AsNestedTypeReference
            Get
                Return Nothing
            End Get
        End Property

        Private ReadOnly Property ITypeReferenceAsSpecializedNestedTypeReference As Cci.ISpecializedNestedTypeReference Implements Cci.ITypeReference.AsSpecializedNestedTypeReference
            Get
                Return Nothing
            End Get
        End Property

        Private Function ITypeReferenceAsTypeDefinition(context As EmitContext) As Cci.ITypeDefinition Implements Cci.ITypeReference.AsTypeDefinition
            Return Nothing
        End Function

        Private Function IReferenceGetAttributes(context As EmitContext) As IEnumerable(Of Cci.ICustomAttribute) Implements Cci.IReference.GetAttributes
            Return SpecializedCollections.EmptyEnumerable(Of Cci.ICustomAttribute)()
        End Function

        Private Sub IReferenceDispatch(visitor As Cci.MetadataVisitor) Implements Cci.IReference.Dispatch
            visitor.Visit(DirectCast(Me, Cci.IPointerTypeReference))
        End Sub

        Private Function IReferenceAsDefinition(context As EmitContext) As Cci.IDefinition Implements Cci.IReference.AsDefinition
            Return Nothing
        End Function

        Private Function IReferenceGetInternalSymbol() As CodeAnalysis.Symbols.ISymbolInternal Implements Cci.IReference.GetInternalSymbol
            Return Nothing
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Throw ExceptionUtilities.Unreachable
        End Function

        Public Overrides Function GetHashCode() As Integer
            Throw ExceptionUtilities.Unreachable
        End Function
    End Class
End Namespace
