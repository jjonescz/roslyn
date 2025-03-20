// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests;

public sealed class IgnoredDirectiveParsingTests(ITestOutputHelper output) : ParsingTests(output)
{
    private const string FeatureName = "FileBasedProgram";

    [Theory, CombinatorialData]
    public void FeatureFlag(bool script)
    {
        var options = script ? TestOptions.Script : TestOptions.Regular;

        var source = """
            #!xyz
            #:name value
            """;

        VerifyTrivia();
        UsingTree(source, options,
            // (2,2): error CS9501: '#:' directives can be only used in file-based programs ('/feature:FileBasedProgram')
            // #:name value
            Diagnostic(ErrorCode.ERR_PPIgnoredNeedsFileBasedProgram, ":").WithLocation(2, 2));

        N(SyntaxKind.CompilationUnit);
        {
            N(SyntaxKind.EndOfFileToken);
            {
                L(SyntaxKind.ShebangDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ExclamationToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "xyz");
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "name value");
                    }
                }
            }
        }
        EOF();

        UsingTree(source, options.WithFeature(FeatureName));

        N(SyntaxKind.CompilationUnit);
        {
            N(SyntaxKind.EndOfFileToken);
            {
                L(SyntaxKind.ShebangDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ExclamationToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "xyz");
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "name value");
                    }
                }
            }
        }
        EOF();
    }

    [Theory, CombinatorialData]
    public void ShebangNotFirst(bool script, bool featureFlag)
    {
        var options = script ? TestOptions.Script : TestOptions.Regular;

        if (featureFlag)
        {
            options = options.WithFeature(FeatureName);
        }

        var source = """
             #!xyz
            """;

        VerifyTrivia();
        UsingTree(source, options,
            // (1,2): error CS1024: Preprocessor directive expected
            //  #!xyz
            Diagnostic(ErrorCode.ERR_PPDirectiveExpected, "#").WithLocation(1, 2));

        N(SyntaxKind.CompilationUnit);
        {
            N(SyntaxKind.EndOfFileToken);
            {
                L(SyntaxKind.WhitespaceTrivia, " ");
                L(SyntaxKind.BadDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    M(SyntaxKind.IdentifierToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.SkippedTokensTrivia);
                        {
                            N(SyntaxKind.ExclamationToken);
                            N(SyntaxKind.IdentifierToken, "xyz");
                        }
                    }
                }
            }
        }
        EOF();
    }

    [Fact]
    public void AfterToken()
    {
        var source = """
            #:x
            M();
            #:y
            """;

        VerifyTrivia();
        UsingTree(source, TestOptions.Regular.WithFeature(FeatureName),
            // (3,2): error CS9500: '#:' directives cannot be after first token in file
            // #:y
            Diagnostic(ErrorCode.ERR_PPIgnoredFollowsToken, ":").WithLocation(3, 2));

        N(SyntaxKind.CompilationUnit);
        {
            N(SyntaxKind.GlobalStatement);
            {
                N(SyntaxKind.ExpressionStatement);
                {
                    N(SyntaxKind.InvocationExpression);
                    {
                        N(SyntaxKind.IdentifierName);
                        {
                            N(SyntaxKind.IdentifierToken, "M");
                            {
                                L(SyntaxKind.IgnoredDirectiveTrivia);
                                {
                                    N(SyntaxKind.HashToken);
                                    N(SyntaxKind.ColonToken);
                                    N(SyntaxKind.EndOfDirectiveToken);
                                    {
                                        L(SyntaxKind.PreprocessingMessageTrivia, "x");
                                        T(SyntaxKind.EndOfLineTrivia, "\n");
                                    }
                                }
                            }
                        }
                        N(SyntaxKind.ArgumentList);
                        {
                            N(SyntaxKind.OpenParenToken);
                            N(SyntaxKind.CloseParenToken);
                        }
                    }
                    N(SyntaxKind.SemicolonToken);
                    {
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
            }
            N(SyntaxKind.EndOfFileToken);
            {
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "y");
                    }
                }
            }
        }
        EOF();
    }

    [Fact]
    public void AfterIf()
    {
        var source = """
            #:x
            #if X
            #:y
            #endif
            #:z
            """;

        VerifyTrivia();
        UsingTree(source, TestOptions.Regular.WithFeature(FeatureName),
            // (3,2): error CS9502: '#:' directives cannot be after '#if'
            // #:y
            Diagnostic(ErrorCode.ERR_PPIgnoredFollowsIf, ":").WithLocation(3, 2),
            // (5,2): error CS9502: '#:' directives cannot be after '#if'
            // #:z
            Diagnostic(ErrorCode.ERR_PPIgnoredFollowsIf, ":").WithLocation(5, 2));

        N(SyntaxKind.CompilationUnit);
        {
            N(SyntaxKind.EndOfFileToken);
            {
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "x");
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.IfDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.IfKeyword);
                    {
                        T(SyntaxKind.WhitespaceTrivia, " ");
                    }
                    N(SyntaxKind.IdentifierName);
                    {
                        N(SyntaxKind.IdentifierToken, "X");
                    }
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "y");
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.EndIfDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.EndIfKeyword);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        T(SyntaxKind.EndOfLineTrivia, "\n");
                    }
                }
                L(SyntaxKind.IgnoredDirectiveTrivia);
                {
                    N(SyntaxKind.HashToken);
                    N(SyntaxKind.ColonToken);
                    N(SyntaxKind.EndOfDirectiveToken);
                    {
                        L(SyntaxKind.PreprocessingMessageTrivia, "z");
                    }
                }
            }
        }
        EOF();
    }
}
