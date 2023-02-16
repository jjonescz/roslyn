// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.CSharp.UnitTests;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities.TestGenerators;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.SourceGeneration
{
    public class SyntaxTreeChangeTests : CSharpTestBase
    {
        internal class IntComparer : IEqualityComparer<int>
        {
            public bool Equals(int x, int y) => x == y;
            public int GetHashCode(int obj) => obj.GetHashCode();
        }

        [Fact]
        public void SimplerTest()
        {
            var source1a = """
                class C111 { }
                """;
            var source1b = """
                [X] class C111 { }
                """;
            var source2 = """
                [Y] class C2 { }
                """;

            var parseOptions = TestOptions.Regular;
            var comp = CreateCompilation(new[] { source1a, source2 }, parseOptions: parseOptions);

            var counter1 = 0;
            var counter2 = 0;
            var counter3 = 0;

            var generator = new IncrementalGeneratorWrapper(new PipelineCallbackGenerator(ctx =>
            {
                var className = ctx.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax { Identifier.ValueText: "C2" },
                    static (c, _) => ((ClassDeclarationSyntax)c.Node).Identifier.ValueText.Length);

                var simple = ctx.SyntaxProvider.ForAttributeWithSimpleName("Y",
                    static (node, _) => node is ClassDeclarationSyntax { Identifier.ValueText: "C2" })
                    .SelectMany(static (t, _) => t.matches)
                    .Select(static (n, _) => ((ClassDeclarationSyntax)n).Identifier.ValueText.Length);

                var metadata = ctx.SyntaxProvider.ForAttributeWithMetadataName("Y",
                    static (node, _) => node is ClassDeclarationSyntax { Identifier.ValueText: "C2" },
                    static (c, _) => ((ClassDeclarationSyntax)c.TargetNode).Identifier.ValueText.Length);

                ctx.RegisterSourceOutput(className, (ctx, value) =>
                {
                    counter1++;
                    Assert.Equal(2, value);
                });

                ctx.RegisterSourceOutput(simple, (ctx, value) =>
                {
                    counter2++;
                    Assert.Equal(2, value);
                });

                ctx.RegisterSourceOutput(metadata, (ctx, value) =>
                {
                    counter3++;
                    Assert.Equal(2, value);
                });
            }));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);

            driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out var diagnostics);
            diagnostics.Verify();

            var tree = comp.GetMember("C111").DeclaringSyntaxReferences.Single().SyntaxTree;

            comp = comp.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source1b, parseOptions));
            driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out diagnostics);
            diagnostics.Verify();

            Assert.Equal(1, counter1);
            Assert.Equal(1, counter2);
            Assert.Equal(1, counter3);
        }

        [Fact]
        public void NoRelevantChangeTest()
        {
            (string original, string changed) testClassSource = (
                /* language=cs */ """
            namespace NpgsqlEfSqlGenerator.Example {
                using System.ComponentModel.DataAnnotations;
                class TestClass {
                    public int Id { get;                                 set; }
                    public required string  Name                  { get; set; }
                    public          string? NullableReferenceType { get; set; }
                    public          int?    NullableValueType     { get; set; }
                }
            }
            """,
                                  /* language=cs */ """
            namespace NpgsqlEfSqlGenerator.Example {
                using System.ComponentModel.DataAnnotations;
                class TestClass {
                    [ Key ]
                    public int Id { get;                                 set; }
                    public required string  Name                  { get; set; }
                    public          string? NullableReferenceType { get; set; }
                    public          int?    NullableValueType     { get; set; }
                }
            }
            """);
            string testDependentClassSource =
                // original
                /* language=cs */
                """
            #nullable enable
            namespace NpgsqlEfSqlGenerator.Example {
                using System.ComponentModel.DataAnnotations;
                [ GenerateSql ]
                public class TestDependentClass {
                    [ Key ]
                    public int DependentId { get;                                 set; }
                    public required string  DependentName                  { get; set; }
                    public          string? DependentNullableReferenceType { get; set; }
                    public          int?    DependentNullableValueType     { get; set; }
                }
            }
            """;

            var compilation = CreateCompilation(new[] {
                CSharpSyntaxTree.ParseText(testClassSource.original, path: "./TestClass.cs"),
                CSharpSyntaxTree.ParseText(testDependentClassSource, path: "./TestDependentClass.cs")
            });

            var counter1 = 0;
            var counter2 = 0;

            var generator = new PipelineCallbackGenerator(context =>
            {
                // CreateSyntaxProvider: this will hit the IntComparer
                IncrementalValuesProvider<int> createSyntaxProviderOutputValues =
                        context.SyntaxProvider
                               .CreateSyntaxProvider(
                                   // simple filter for attributes
                                   predicate: static (node, _) =>
                                   {
                                       return node is ClassDeclarationSyntax { Identifier.ValueText: "TestDependentClass" };
                                   },
                                   // filter out attributed classes that we don't care about
                                   transform: static (syntaxContext, _) =>
                                   {
                                       return (syntaxContext.Node as ClassDeclarationSyntax)?.Identifier.ValueText.Length ?? 33;
                                   })
                               .WithComparer(new IntComparer());

                // this will NOT hit the IntComparer
                IncrementalValuesProvider<int> forAttributeWithMetadataNameOutputValues =
                        context.SyntaxProvider
                               .ForAttributeWithMetadataName(
                                   "GenerateSql",
                                   // simple filter for attributes
                                   predicate: static (node, _) =>
                                   {
                                       return node is ClassDeclarationSyntax { Identifier.ValueText: "TestDependentClass" };
                                   },
                                   // filter out attributed classes that we don't care about
                                   transform: static (syntaxContext, _) =>
                                   {
                                       return (syntaxContext.TargetNode as ClassDeclarationSyntax)?.Identifier.ValueText.Length ?? 33;
                                   }).WithComparer(new IntComparer());

                context.RegisterSourceOutput(createSyntaxProviderOutputValues, (SourceProductionContext sourceProductionContext, int value) =>
                {
                    // ONLY EXECUTED ONCE
                    counter1++;
                    // generate the source code and add it to the output
                    string result = $"public class s_{value} {{ }}";
                    sourceProductionContext.AddSource($"CreateSyntaxProviderOutputValues_TEST_STRING_{(value)}.g.cs", SourceText.From(result, Encoding.UTF8));
                });

                context.RegisterSourceOutput(forAttributeWithMetadataNameOutputValues, (SourceProductionContext sourceProductionContext, int value) =>
                {
                    // EXECUTED TWICE
                    counter2++;
                    // generate the source code and add it to the output
                    string result = $"public class s_{value} {{ }}";
                    sourceProductionContext.AddSource($"ForAttributeWithMetadataNameOutputValues_{(value)}.g.cs", SourceText.From(result, Encoding.UTF8));
                });
            });

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
            Assert.Empty(diagnostics);

            compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees[0], CSharpSyntaxTree.ParseText(testClassSource.changed, path: "./TestClass.cs"));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out diagnostics);
            Assert.Empty(diagnostics);

            Assert.Equal(1, counter1);
            Assert.Equal(1, counter2);
        }
    }
}
