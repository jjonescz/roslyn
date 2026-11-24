// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using LSP = Roslyn.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.UnitTests.Completion;

public sealed class ExternalCompletionProviderTests(ITestOutputHelper output) : AbstractLanguageServerProtocolTests(output)
{
    protected override TestComposition Composition => base.Composition.AddParts(typeof(ExternalProvider));

    [Fact]
    public async Task MetadataTriggers_AreAdvertisedAsync()
    {
        await using var server = await CreateTestLspServerAsync("", mutatingLspWorkspace: false);
        Assert.Contains("\u00A7", server.GetServerCapabilities().CompletionProvider!.TriggerCharacters!);
    }

    [Fact]
    public async Task DynamicItems_RetriggerAndReplaceWholeTokenAsync()
    {
        await using var server = await CreateTestLspServerAsync("P{|caret:|}ackageSuffix", mutatingLspWorkspace: false);
        var location = server.GetLocations("caret").Single();
        var request = new LSP.CompletionParams
        {
            TextDocument = CreateTextDocumentIdentifier(location.DocumentUri),
            Position = location.Range.Start,
            Context = new LSP.CompletionContext { TriggerKind = LSP.CompletionTriggerKind.Invoked },
        };
        var result = await CompletionTests.RunGetCompletionsAsync(server, request);
        var item = Assert.Single(result.Items);
        Assert.True(result.IsIncomplete);
        Assert.Equal("PackageOne", item.Label);
        Assert.Equal(13, item.TextEdit!.Value.First.Range.End.Character);

        var document = server.GetCurrentSolution().Projects.Single().Documents.Single();
        await server.TestWorkspace.ChangeDocumentAsync(document.Id, SourceText.From("Pa"));
        request.Position = new LSP.Position(0, 2);
        request.Context.TriggerKind = LSP.CompletionTriggerKind.TriggerForIncompleteCompletions;
        result = await CompletionTests.RunGetCompletionsAsync(server, request);
        Assert.Equal("PackageTwo", Assert.Single(result.Items).Label);
        Assert.True(result.IsIncomplete);
    }

    [ExportCompletionProvider(nameof(ExternalProvider), LanguageNames.CSharp)]
    [ExportMetadata("TriggerCharacters", new char[] { '\u00A7' })]
    [Shared, PartNotDiscoverable]
    private sealed class ExternalProvider : CompletionProvider
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public ExternalProvider() { }

        public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options) => true;

        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            var text = await context.Document.GetTextAsync(context.CancellationToken);
            context.CompletionListSpan = new TextSpan(0, text.Length);
            context.IsExclusive = true;
            context.AddItem(CompletionItem.Create(
                context.Position == 1 ? "PackageOne" : "PackageTwo",
                properties: ImmutableDictionary<string, string>.Empty
                    .Add("RequiresRefreshOnTyping", "true")
                    .Add("UseFullReplacementSpan", "true")));
        }

        public override async Task<CompletionChange> GetChangeAsync(Document document, CompletionItem item, char? commitKey, CancellationToken cancellationToken)
            => CompletionChange.Create(new TextChange(new TextSpan(0, (await document.GetTextAsync(cancellationToken)).Length), item.DisplayText));
    }
}
