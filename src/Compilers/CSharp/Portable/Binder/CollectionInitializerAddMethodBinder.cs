// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Keeps track of the type for which we are trying to bind a collection initializer.
/// </summary>
internal sealed class CollectionInitializerAddMethodBinder : Binder
{
    private readonly TypeSymbol _collectionType;

    internal CollectionInitializerAddMethodBinder(TypeSymbol collectionType, Binder next)
        : base(next, next.Flags | BinderFlags.CollectionInitializerAddMethod)
    {
        Debug.Assert(collectionType is not null);

        _collectionType = collectionType;
    }

    internal override TypeSymbol CollectionInitializerTypeInProgress => _collectionType;
}
