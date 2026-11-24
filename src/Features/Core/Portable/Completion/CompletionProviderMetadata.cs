// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Host.Mef;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Completion.Providers;

internal sealed class CompletionProviderMetadata(IDictionary<string, object> data) : OrderableLanguageMetadata(data)
{
    public string[]? Roles { get; } = (string[]?)data.GetValueOrDefault("Roles")
            ?? (string[]?)data.GetValueOrDefault("TextViewRoles");

    public char[] TriggerCharacters { get; } = data.GetValueOrDefault("TriggerCharacters") switch
    {
        char[] characters => characters,
        // Explicit ExportMetadata on providers compiled against an older attribute can coexist
        // with the new attribute's null default in a newer host's MEF catalog.
        char[][] groups => [.. groups.Where(group => group is not null).SelectMany(group => group)],
        null => [],
        _ => throw new System.ArgumentException("Completion trigger metadata must contain characters."),
    };
}
