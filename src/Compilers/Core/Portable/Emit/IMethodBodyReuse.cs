// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.Symbols;

namespace Microsoft.CodeAnalysis.Emit;

internal interface IMethodBodyReuse
{
    IMethodBodyReuseSession CreateSession(CommonPEModuleBuilder moduleBuilder);
}

internal interface IMethodBodyReuseSession
{
    bool ShouldCompile(ISymbolInternal symbol);

    bool TryReuseMethodBody(
        IMethodSymbolInternal method,
        CommonPEModuleBuilder moduleBuilder,
        DiagnosticBag diagnostics);

    void RecordEmittedBody(bool reused);

    MethodBodyReuseStatistics Complete(bool succeeded);
}
