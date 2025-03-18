// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests;

public sealed class IgnoredDirectiveParsingTests(ITestOutputHelper output) : ParsingTests(output)
{
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

        UsingTree(source, options.WithFeature("FileBasedProgram"));

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
}
