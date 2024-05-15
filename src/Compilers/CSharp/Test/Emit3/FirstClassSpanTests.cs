// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests;

public class FirstClassSpanTests : CSharpTestBase
{
    public static TheoryData<LanguageVersion> LangVersions()
    {
        return new TheoryData<LanguageVersion>()
        {
            LanguageVersion.CSharp12,
            LanguageVersionFacts.CSharpNext,
            LanguageVersion.Preview,
        };
    }

    [Theory, CombinatorialData]
    public void Conversion_Array_Span_Implicit(
        [CombinatorialValues("Span", "ReadOnlySpan")] string destination,
        bool cast)
    {
        var source = $$"""
            using System;
            {{destination}}<int> s = {{(cast ? $"({destination}<int>)" : "")}}arr();
            report(s);
            static int[] arr() => new int[] { 1, 2, 3 };
            static void report({{destination}}<int> s) { foreach (var x in s) { Console.Write(x); } }
            """;

        var expectedOutput = "123";

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12);
        var verifier = CompileAndVerify(comp, expectedOutput: expectedOutput);
        verifier.VerifyDiagnostics();
        verifier.VerifyIL("<top-level-statements-entry-point>", $$"""
            {
              // Code size       16 (0x10)
              .maxstack  1
              IL_0000:  call       "int[] Program.<<Main>$>g__arr|0_0()"
              IL_0005:  call       "System.{{destination}}<int> System.{{destination}}<int>.op_Implicit(int[])"
              IL_000a:  call       "void Program.<<Main>$>g__report|0_1(System.{{destination}}<int>)"
              IL_000f:  ret
            }
            """);

        var expectedIl = $$"""
            {
              // Code size       16 (0x10)
              .maxstack  1
              IL_0000:  call       "int[] Program.<<Main>$>g__arr|0_0()"
              IL_0005:  newobj     "System.{{destination}}<int>..ctor(int[])"
              IL_000a:  call       "void Program.<<Main>$>g__report|0_1(System.{{destination}}<int>)"
              IL_000f:  ret
            }
            """;

        comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput);
        verifier.VerifyDiagnostics();
        verifier.VerifyIL("<top-level-statements-entry-point>", expectedIl);

        comp = CreateCompilationWithSpan(source);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput);
        verifier.VerifyDiagnostics();
        verifier.VerifyIL("<top-level-statements-entry-point>", expectedIl);
    }

    [Fact]
    public void Conversion_Array_Span_Implicit_MissingCtor()
    {
        var source = """
            using System;
            Span<int> s = arr();
            static int[] arr() => new int[] { 1, 2, 3 };
            """;

        var comp = CreateCompilationWithSpan(source);
        comp.MakeMemberMissing(WellKnownMember.System_Span_T__ctor_Array);
        comp.VerifyDiagnostics(
            // (2,15): error CS0656: Missing compiler required member 'System.Span`1..ctor'
            // Span<int> s = arr();
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "arr()").WithArguments("System.Span`1", ".ctor").WithLocation(2, 15));
    }

    [Fact]
    public void Conversion_Array_Span_Implicit_SemanticModel()
    {
        var source = """
            class C
            {
                System.Span<int> M(int[] arg) { return arg; }
            }
            """;

        var comp = CreateCompilationWithSpan(source);
        var tree = comp.SyntaxTrees.Single();
        var model = comp.GetSemanticModel(tree);

        var arg = tree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression;
        Assert.Equal("arg", arg!.ToString());

        var argType = model.GetTypeInfo(arg);
        Assert.Equal("System.Int32[]", argType.Type.ToTestDisplayString());
        Assert.Equal("System.Span<System.Int32>", argType.ConvertedType.ToTestDisplayString());

        var argConv = model.GetConversion(arg);
        Assert.True(argConv.IsSpan);
        Assert.True(argConv.IsImplicit);
        Assert.False(argConv.IsUserDefined);
        Assert.False(argConv.IsIdentity);
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_Implicit_UnrelatedElementType(LanguageVersion langVersion)
    {
        var source = """
            class C
            {
                System.Span<string> M(int[] arg) => arg;
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion)).VerifyDiagnostics(
            // (3,41): error CS0029: Cannot implicitly convert type 'int[]' to 'System.Span<string>'
            //     System.Span<string> M(int[] arg) => arg;
            Diagnostic(ErrorCode.ERR_NoImplicitConv, "arg").WithArguments("int[]", "System.Span<string>").WithLocation(3, 41));
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_Opposite_Implicit(LanguageVersion langVersion)
    {
        var source = """
            class C
            {
                 int[] M(System.Span<int> arg) => arg;
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion)).VerifyDiagnostics(
            // (3,39): error CS0029: Cannot implicitly convert type 'System.Span<int>' to 'int[]'
            //      int[] M(System.Span<int> arg) => arg;
            Diagnostic(ErrorCode.ERR_NoImplicitConv, "arg").WithArguments("System.Span<int>", "int[]").WithLocation(3, 39));
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_Opposite_Explicit(LanguageVersion langVersion)
    {
        var source = """
            class C
            {
                 int[] M(System.Span<int> arg) => (int[])arg;
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion)).VerifyDiagnostics(
            // (3,39): error CS0030: Cannot convert type 'System.Span<int>' to 'int[]'
            //      int[] M(System.Span<int> arg) => (int[])arg;
            Diagnostic(ErrorCode.ERR_NoExplicitConv, "(int[])arg").WithArguments("System.Span<int>", "int[]").WithLocation(3, 39));
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_Opposite_Explicit_UserDefined(LanguageVersion langVersion)
    {
        var source = """
            class C
            {
                 int[] M(System.Span<int> arg) => (int[])arg;
            }

            namespace System
            {
                readonly ref struct Span<T>
                {
                    public static explicit operator T[](Span<T> span) => throw null;
                }
            }
            """;
        var verifier = CompileAndVerify(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion));
        verifier.VerifyDiagnostics();
        verifier.VerifyIL("C.M", """
            {
              // Code size        7 (0x7)
              .maxstack  1
              IL_0000:  ldarg.1
              IL_0001:  call       "int[] System.Span<int>.op_Explicit(System.Span<int>)"
              IL_0006:  ret
            }
            """);
    }

    [Theory, CombinatorialData]
    public void Conversion_Array_Span_ThroughUserImplicit(
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
    public void Conversion_Array_Span_ThroughUserImplicit_MissingCtor()
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
    public void Conversion_Array_ReadOnlySpan_ThroughUserImplicit_MissingCtor()
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

    [Fact]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Implicit()
    {
        var source = """
            using System;

            var arr = new int[] { 7, 8, 9 };
            arr.E();

            static class C
            {
                 public static void E(this Span<int> arg) => Console.Write(arg[1]);
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12).VerifyDiagnostics(
            // (4,1): error CS1929: 'int[]' does not contain a definition for 'E' and the best extension method overload 'C.E(Span<int>)' requires a receiver of type 'System.Span<int>'
            // arr.E();
            Diagnostic(ErrorCode.ERR_BadInstanceArgType, "arr").WithArguments("int[]", "E", "C.E(System.Span<int>)", "System.Span<int>").WithLocation(4, 1));

        var expectedOutput = "8";

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();

        comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Explicit(LanguageVersion langVersion)
    {
        var source = """
            using System;

            var arr = new int[] { 7, 8, 9 };
            ((Span<int>)arr).E();

            static class C
            {
                 public static void E(this Span<int> arg) => Console.Write(arg[1]);
            }
            """;
        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion));
        CompileAndVerify(comp, expectedOutput: "8").VerifyDiagnostics();
    }
}
