// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.CodeGen;

internal partial class CodeGenerator
{
    private static bool MightEscapeTemporaryRefs(BoundCall node, bool used, AddressKind? receiverAddressKind)
    {
        return MightEscapeTemporaryRefs(
            used: used,
            returnType: node.Type,
            returnRefKind: node.Method.RefKind,
            receiverType: !node.Method.RequiresInstanceReceiver ? null : node.ReceiverOpt?.Type,
            receiverScope: node.Method.TryGetThisParameter(out var thisParameter) ? thisParameter?.EffectiveScope : null,
            receiverAddressKind: receiverAddressKind,
            isReceiverReadOnly: node.Method.IsEffectivelyReadOnly,
            parameters: node.Method.Parameters,
            arguments: node.Arguments);
    }

    private static bool MightEscapeTemporaryRefs(BoundObjectCreationExpression node, bool used)
    {
        return MightEscapeTemporaryRefs(
            used: used,
            returnType: node.Type,
            returnRefKind: RefKind.None,
            receiverType: null,
            receiverScope: null,
            receiverAddressKind: null,
            isReceiverReadOnly: false,
            parameters: node.Constructor.Parameters,
            arguments: node.Arguments);
    }

    private static bool MightEscapeTemporaryRefs(BoundFunctionPointerInvocation node, bool used)
    {
        FunctionPointerMethodSymbol method = node.FunctionPointer.Signature;
        return MightEscapeTemporaryRefs(
            used: used,
            returnType: node.Type,
            returnRefKind: method.RefKind,
            receiverType: null,
            receiverScope: null,
            receiverAddressKind: null,
            isReceiverReadOnly: false,
            parameters: method.Parameters,
            arguments: node.Arguments);
    }

    private static bool MightEscapeTemporaryRefs(
        bool used,
        TypeSymbol returnType,
        RefKind returnRefKind,
        TypeSymbol? receiverType,
        ScopedKind? receiverScope,
        AddressKind? receiverAddressKind,
        bool isReceiverReadOnly,
        ImmutableArray<ParameterSymbol> parameters,
        ImmutableArray<BoundExpression> arguments)
    {
        Debug.Assert(receiverAddressKind is null || receiverType is not null);

        // number of outputs that can capture references
        int writableRefs = 0;
        // number of inputs that can contain references
        int readableRefs = 0;

        if (used && (returnRefKind != RefKind.None || returnType.IsRefLikeOrAllowsRefLikeType()))
        {
            // If returning by reference or returning a ref struct, the result might capture references.
            writableRefs++;
        }

        if (receiverType is not null)
        {
            receiverScope ??= ScopedKind.None;
            if (receiverAddressKind is { } a && !IsAnyReadOnly(a) && receiverScope == ScopedKind.None)
            {
                writableRefs++;
                readableRefs++;
            }
            else if (receiverType.IsRefLikeOrAllowsRefLikeType() && receiverScope != ScopedKind.ScopedValue)
            {
                if (isReceiverReadOnly || receiverType.IsReadOnly)
                {
                    readableRefs++;
                }
                else
                {
                    writableRefs++;
                    readableRefs++;
                }
            }
            else if (receiverAddressKind != null && receiverScope == ScopedKind.None)
            {
                readableRefs++;
            }
        }

        if (shouldReturnTrue(writableRefs, readableRefs))
        {
            return true;
        }

        for (var arg = 0; arg < arguments.Length; arg++)
        {
            var parameter = parameters[arg];

            if (parameter.RefKind.IsWritableReference() && parameter.EffectiveScope == ScopedKind.None)
            {
                writableRefs++;
                readableRefs++;
            }
            else if (parameter.Type.IsRefLikeOrAllowsRefLikeType() && parameter.EffectiveScope != ScopedKind.ScopedValue)
            {
                if (parameter.Type.IsReadOnly || !parameter.RefKind.IsWritableReference())
                {
                    readableRefs++;
                }
                else
                {
                    writableRefs++;
                    readableRefs++;
                }
            }
            else if (parameter.RefKind != RefKind.None && parameter.EffectiveScope == ScopedKind.None)
            {
                readableRefs++;
            }

            if (shouldReturnTrue(writableRefs, readableRefs))
            {
                return true;
            }
        }

        return false;

        static bool shouldReturnTrue(int writableRefs, int readableRefs)
        {
            // If there is at least one output and at least one input, a reference can be captured.
            return writableRefs > 0 && readableRefs > 0;
        }
    }
}
