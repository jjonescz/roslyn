// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.CodeGen;

internal partial class CodeGenerator
{
    private static bool MightEscapeTemporaryRefs(BoundCall node, AddressKind? receiverAddressKind)
    {
        return MightEscapeTemporaryRefs(
            returnType: node.Type,
            returnRefKind: node.Method.RefKind,
            receiverType: node.ReceiverOpt?.Type,
            receiverAddressKind: receiverAddressKind,
            isReceiverReadOnly: node.Method.IsEffectivelyReadOnly,
            parameters: node.Method.Parameters,
            arguments: node.Arguments,
            argsToParamsOpt: node.ArgsToParamsOpt,
            expanded: node.Expanded);
    }

    private static bool MightEscapeTemporaryRefs(BoundObjectCreationExpression node)
    {
        return MightEscapeTemporaryRefs(
            returnType: node.Type,
            returnRefKind: RefKind.None,
            receiverType: null,
            receiverAddressKind: null,
            isReceiverReadOnly: false,
            parameters: node.Constructor.Parameters,
            arguments: node.Arguments,
            argsToParamsOpt: node.ArgsToParamsOpt,
            expanded: node.Expanded);
    }

    private static bool MightEscapeTemporaryRefs(BoundFunctionPointerInvocation node)
    {
        FunctionPointerMethodSymbol method = node.FunctionPointer.Signature;
        return MightEscapeTemporaryRefs(
            returnType: node.Type,
            returnRefKind: method.RefKind,
            receiverType: null,
            receiverAddressKind: null,
            isReceiverReadOnly: false,
            parameters: method.Parameters,
            arguments: node.Arguments,
            argsToParamsOpt: default,
            expanded: false);
    }

    private static bool MightEscapeTemporaryRefs(
        TypeSymbol returnType,
        RefKind returnRefKind,
        TypeSymbol? receiverType,
        AddressKind? receiverAddressKind,
        bool isReceiverReadOnly,
        ImmutableArray<ParameterSymbol> parameters,
        ImmutableArray<BoundExpression> arguments,
        ImmutableArray<int> argsToParamsOpt,
        bool expanded)
    {
        int writableRefs = 0;
        int readonlyRefs = 0;

        if (returnRefKind != RefKind.None || returnType.IsRefLikeOrAllowsRefLikeType())
        {
            writableRefs++;
        }

        if (receiverType is not null)
        {
            if (receiverAddressKind is { } a && IsAnyReadOnly(a))
            {
                writableRefs++;
            }
            else if (receiverType.IsRefLikeOrAllowsRefLikeType())
            {
                if (isReceiverReadOnly || receiverType.IsReadOnly)
                {
                    readonlyRefs++;
                }
                else
                {
                    writableRefs++;
                }
            }
            else if (receiverAddressKind != null)
            {
                readonlyRefs++;
            }
        }

        if (shouldReturnTrue(writableRefs, readonlyRefs))
        {
            return true;
        }

        for (var arg = 0; arg < arguments.Length; arg++)
        {
            var parameter = Binder.GetCorrespondingParameter(
                arg,
                parameters,
                argsToParamsOpt,
                expanded);

            if (parameter is not null)
            {
                if (parameter.RefKind.IsWritableReference())
                {
                    writableRefs++;
                }
                else if (parameter.Type.IsRefLikeOrAllowsRefLikeType())
                {
                    if (parameter.Type.IsReadOnly)
                    {
                        readonlyRefs++;
                    }
                    else
                    {
                        writableRefs++;
                    }
                }
                else if (parameter.RefKind != RefKind.None)
                {
                    readonlyRefs++;
                }
            }

            if (shouldReturnTrue(writableRefs, readonlyRefs))
            {
                return true;
            }
        }

        return false;

        static bool shouldReturnTrue(int writableRefs, int readonlyRefs)
        {
            return writableRefs > 0 && (writableRefs + readonlyRefs) > 1;
        }
    }
}
