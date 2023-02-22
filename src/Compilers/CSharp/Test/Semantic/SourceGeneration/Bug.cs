// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.CSharp.UnitTests;
using Roslyn.Test.Utilities;
using Roslyn.Test.Utilities.TestGenerators;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.SourceGeneration
{
    public class Bug : CSharpTestBase
    {
        [Fact, WorkItem(61162, "https://github.com/dotnet/roslyn/issues/61162")]
        public void RoslynIssue61162()
        {
            var generator = new IncrementalGeneratorWrapper(new PipelineCallbackGenerator(ctx =>
            {
                var invokedMethodsProvider = ctx.SyntaxProvider.CreateSyntaxProvider(
                        predicate: (node, _) => node is InvocationExpressionSyntax,
                        transform: (ctx, ct) => ctx.SemanticModel.GetSymbolInfo(ctx.Node, ct).Symbol?.Name ?? "<< method not found >>")
                    .Collect();

                ctx.RegisterSourceOutput(invokedMethodsProvider, (SourceProductionContext spc, ImmutableArray<string> invokedMethods) =>
                {
                    var src = new StringBuilder();
                    foreach (var method in invokedMethods)
                    {
                        src.AppendLine("// " + method);
                    }
                    spc.AddSource("InvokedMethods.g.cs", src.ToString());
                });
            }));

            var source = """
                System.Console.WriteLine();
                System.Console.WriteLine();
                System.Console.WriteLine();
                System.Console.WriteLine();
                """;
            var parseOptions = TestOptions.RegularPreview;
            Compilation compilation = CreateCompilation(source, options: TestOptions.DebugExeThrowing, parseOptions: parseOptions);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
            verify(ref driver, compilation, """
                // WriteLine
                // WriteLine
                // WriteLine
                // WriteLine

                """);

            replace(ref compilation, parseOptions, """
                System.Console.WriteLine();
                System.Console.WriteLine();
                """);
            verify(ref driver, compilation, """
                // WriteLine
                // WriteLine

                """);

            replace(ref compilation, parseOptions, "_ = 0;");
            verify(ref driver, compilation, """

                """);

            static void verify(ref GeneratorDriver driver, Compilation compilation, string generatedContent)
            {
                driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
                outputCompilation.VerifyDiagnostics();
                generatorDiagnostics.Verify();
                var generatedTree = driver.GetRunResult().GeneratedTrees.Single();
                AssertEx.EqualOrDiff(generatedContent, generatedTree.ToString());
            }

            static void replace(ref Compilation compilation, CSharpParseOptions parseOptions, string source)
            {
                compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(source, parseOptions));
            }
        }
    }
}
