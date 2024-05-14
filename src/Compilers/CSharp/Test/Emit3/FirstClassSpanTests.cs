// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests;

public class FirstClassSpanTests : CSharpTestBase
{
    [Theory, CombinatorialData]
    public void Conversion_Array_Span_UserImplicit(
        [CombinatorialValues("Span", "ReadOnlySpan")] string destination)
    {
        var source = $$"""
            using System;

            D.M(new C());

            class C
            {
                public static implicit operator int[](C c) => new int[] { 4, 5, 6 };
            }

            static class D
            {
                public static void M({{destination}}<int> xs)
                {
                    foreach (var x in xs)
                    {
                        Console.Write(x);
                    }
                }
            }
            """;

        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12).VerifyDiagnostics(
            // (3,5): error CS1503: Argument 1: cannot convert from 'C' to 'System.Span<int>'
            // D.M(new C());
            Diagnostic(ErrorCode.ERR_BadArgType, "new C()").WithArguments("1", "C", $"System.{destination}<int>").WithLocation(3, 5));

        var expectedOutput = "456";

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();

        comp = CreateCompilationWithSpan(source);
        CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
    }

    [Fact]
    public void Conversion_Array_Span_MissingCtor()
    {
        var source = """
            using System;

            D.M(new C());

            class C
            {
                public static implicit operator int[](C c) => new int[] { 4, 5, 6 };
            }

            static class D
            {
                public static void M(Span<int> xs)
                {
                    foreach (var x in xs)
                    {
                        Console.Write(x);
                    }
                }
            }
            """;

        var expectedDiagnostics = new[]
        {
            // (3,5): error CS1503: Argument 1: cannot convert from 'C' to 'System.Span<int>'
            // D.M(new C());
            Diagnostic(ErrorCode.ERR_BadArgType, "new C()").WithArguments("1", "C", "System.Span<int>").WithLocation(3, 5)
        };

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.Regular12, expectedDiagnostics);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.Regular12, expectedDiagnostics);

        expectedDiagnostics = [
            // (3,5): error CS0656: Missing compiler required member 'System.Span`1..ctor'
            // D.M(new C());
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "new C()").WithArguments("System.Span`1", ".ctor").WithLocation(3, 5)
        ];

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.RegularNext);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.RegularNext, expectedDiagnostics);

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.RegularPreview);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.RegularPreview, expectedDiagnostics);

        void verifyWithMissing(WellKnownMember member, CSharpParseOptions parseOptions, params DiagnosticDescription[] expected)
        {
            var comp = CreateCompilationWithSpan(source, parseOptions: parseOptions);
            comp.MakeMemberMissing(member);
            if (expected.Length == 0)
            {
                CompileAndVerify(comp, expectedOutput: "456").VerifyDiagnostics();
            }
            else
            {
                comp.VerifyDiagnostics(expected);
            }
        }
    }

    [Fact]
    public void Conversion_Array_ReadOnlySpan_MissingCtor()
    {
        var source = """
            using System;

            D.M(new C());

            class C
            {
                public static implicit operator int[](C c) => new int[] { 4, 5, 6 };
            }

            static class D
            {
                public static void M(ReadOnlySpan<int> xs)
                {
                    foreach (var x in xs)
                    {
                        Console.Write(x);
                    }
                }
            }
            """;

        var expectedDiagnostics = new[]
        {
            // (3,5): error CS1503: Argument 1: cannot convert from 'C' to 'System.ReadOnlySpan<int>'
            // D.M(new C());
            Diagnostic(ErrorCode.ERR_BadArgType, "new C()").WithArguments("1", "C", "System.ReadOnlySpan<int>").WithLocation(3, 5)
        };

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.Regular12, expectedDiagnostics);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.Regular12, expectedDiagnostics);

        expectedDiagnostics = [
            // (3,5): error CS0656: Missing compiler required member 'System.ReadOnlySpan`1..ctor'
            // D.M(new C());
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "new C()").WithArguments("System.ReadOnlySpan`1", ".ctor").WithLocation(3, 5)
        ];

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.RegularNext, expectedDiagnostics);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.RegularNext);

        verifyWithMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array, TestOptions.RegularPreview, expectedDiagnostics);
        verifyWithMissing(WellKnownMember.System_Span_T__ctor_Array, TestOptions.RegularPreview);

        void verifyWithMissing(WellKnownMember member, CSharpParseOptions parseOptions, params DiagnosticDescription[] expected)
        {
            var comp = CreateCompilationWithSpan(source, parseOptions: parseOptions);
            comp.MakeMemberMissing(member);
            if (expected.Length == 0)
            {
                CompileAndVerify(comp, expectedOutput: "456").VerifyDiagnostics();
            }
            else
            {
                comp.VerifyDiagnostics(expected);
            }
        }
    }
}
