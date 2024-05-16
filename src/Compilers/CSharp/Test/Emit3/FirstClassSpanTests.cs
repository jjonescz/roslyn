// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
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

    [Fact, WorkItem("https://github.com/dotnet/runtime/issues/101261")]
    public void Example_StringValuesAmbiguity()
    {
        var source = """
            using System;

            Console.Write(C.M(new StringValues()));

            static class C
            {
                public static string M(StringValues sv) => StringExtensions.Join(",", sv);
            }

            static class StringExtensions
            {
                public static string Join(string separator, params string[] values) => "array";
                public static string Join(string separator, params ReadOnlySpan<string> values) => "span";
            }

            readonly struct StringValues
            {
                public static implicit operator string(StringValues values) => null;
                public static implicit operator string[](StringValues value) => null;
            }
            """;

        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12).VerifyDiagnostics(
            // (7,65): error CS0121: The call is ambiguous between the following methods or properties: 'StringExtensions.Join(string, params string[])' and 'StringExtensions.Join(string, params ReadOnlySpan<string>)'
            //     public static string M(StringValues sv) => StringExtensions.Join(",", sv);
            Diagnostic(ErrorCode.ERR_AmbigCall, "Join").WithArguments("StringExtensions.Join(string, params string[])", "StringExtensions.Join(string, params System.ReadOnlySpan<string>)").WithLocation(7, 65),
            // (13,49): error CS8652: The feature 'params collections' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
            //     public static string Join(string separator, params ReadOnlySpan<string> values) => "span";
            Diagnostic(ErrorCode.ERR_FeatureInPreview, "params ReadOnlySpan<string> values").WithArguments("params collections").WithLocation(13, 49));

        var expectedOutput = "array";

        var expectedIl = """
            {
              // Code size       17 (0x11)
              .maxstack  2
              IL_0000:  ldstr      ","
              IL_0005:  ldarg.0
              IL_0006:  call       "string[] StringValues.op_Implicit(StringValues)"
              IL_000b:  call       "string StringExtensions.Join(string, params string[])"
              IL_0010:  ret
            }
            """;

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        var verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);

        comp = CreateCompilationWithSpan(source);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);
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
    public void Conversion_Array_Span_Implicit_SpanTwice()
    {
        static string getSpanSource(string output) => $$"""
            namespace System
            {
                public readonly ref struct Span<T>
                {
                    public Span(T[] array) => Console.Write("{{output}}");
                }
            }
            """;

        var spanComp = CreateCompilation(getSpanSource("External"), assemblyName: "Span1")
            .VerifyDiagnostics()
            .EmitToImageReference();

        var source = """
            using System;
            Span<int> s = arr();
            static int[] arr() => new int[] { 1, 2, 3 };
            """;

        var comp = CreateCompilation([source, getSpanSource("Internal")], [spanComp], assemblyName: "Consumer");
        var verifier = CompileAndVerify(comp, expectedOutput: "Internal");
        verifier.VerifyDiagnostics(
            // (2,1): warning CS0436: The type 'Span<T>' in '' conflicts with the imported type 'Span<T>' in 'Span1, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'. Using the type defined in ''.
            // Span<int> s = arr();
            Diagnostic(ErrorCode.WRN_SameFullNameThisAggAgg, "Span<int>").WithArguments("", "System.Span<T>", "Span1, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "System.Span<T>").WithLocation(2, 1));

        verifier.VerifyIL("<top-level-statements-entry-point>", """
            {
              // Code size       12 (0xc)
              .maxstack  1
              IL_0000:  call       "int[] Program.<<Main>$>g__arr|0_0()"
              IL_0005:  newobj     "System.Span<int>..ctor(int[])"
              IL_000a:  pop
              IL_000b:  ret
            }
            """);
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

            C.M(new int[] { 7, 8, 9 });

            static class C
            {
                public static void M(int[] arg) => arg.E();
                public static void E(this Span<int> arg) => Console.Write(arg[1]);
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12).VerifyDiagnostics(
            // (7,40): error CS1929: 'int[]' does not contain a definition for 'E' and the best extension method overload 'C.E(Span<int>)' requires a receiver of type 'System.Span<int>'
            //     public static void M(int[] arg) => arg.E();
            Diagnostic(ErrorCode.ERR_BadInstanceArgType, "arg").WithArguments("int[]", "E", "C.E(System.Span<int>)", "System.Span<int>").WithLocation(7, 40));

        var expectedOutput = "8";

        var expectedIl = """
            {
              // Code size       12 (0xc)
              .maxstack  1
              IL_0000:  ldarg.0
              IL_0001:  newobj     "System.Span<int>..ctor(int[])"
              IL_0006:  call       "void C.E(System.Span<int>)"
              IL_000b:  ret
            }
            """;

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        var verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);

        comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);
    }

    [Fact]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Implicit_MissingCtor()
    {
        var source = """
            using System;
            
            C.M(new int[] { 7, 8, 9 });
            
            static class C
            {
                public static void M(int[] arg) => arg.E();
                public static void E(this Span<int> arg) => Console.Write(arg[1]);
            }
            """;
        var comp = CreateCompilationWithSpan(source);
        comp.MakeMemberMissing(WellKnownMember.System_Span_T__ctor_Array);
        comp.VerifyDiagnostics(
            // (7,40): error CS0656: Missing compiler required member 'System.Span`1..ctor'
            //     public static void M(int[] arg) => arg.E();
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "arg").WithArguments("System.Span`1", ".ctor").WithLocation(7, 40));
    }

    [Fact]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Explicit()
    {
        var source = """
            using System;

            C.M(new int[] { 7, 8, 9 });

            static class C
            {
                public static void M(int[] arg) => ((Span<int>)arg).E();
                public static void E(this Span<int> arg) => Console.Write(arg[1]);
            }
            """;

        var expectedOutput = "8";

        var comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular12);
        var verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", """
            {
              // Code size       12 (0xc)
              .maxstack  1
              IL_0000:  ldarg.0
              IL_0001:  call       "System.Span<int> System.Span<int>.op_Implicit(int[])"
              IL_0006:  call       "void C.E(System.Span<int>)"
              IL_000b:  ret
            }
            """);

        var expectedIl = """
            {
              // Code size       12 (0xc)
              .maxstack  1
              IL_0000:  ldarg.0
              IL_0001:  newobj     "System.Span<int>..ctor(int[])"
              IL_0006:  call       "void C.E(System.Span<int>)"
              IL_000b:  ret
            }
            """;

        comp = CreateCompilationWithSpan(source, parseOptions: TestOptions.RegularNext);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);

        comp = CreateCompilationWithSpan(source);
        verifier = CompileAndVerify(comp, expectedOutput: expectedOutput).VerifyDiagnostics();
        verifier.VerifyIL("C.M", expectedIl);
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Opposite_Implicit(LanguageVersion langVersion)
    {
        var source = """
            static class C
            {
                static void M(System.Span<int> arg) => arg.E();
                static void E(this int[] arg) { }
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion)).VerifyDiagnostics(
            // (3,44): error CS1929: 'Span<int>' does not contain a definition for 'E' and the best extension method overload 'C.E(int[])' requires a receiver of type 'int[]'
            //     static void M(System.Span<int> arg) => arg.E();
            Diagnostic(ErrorCode.ERR_BadInstanceArgType, "arg").WithArguments("System.Span<int>", "E", "C.E(int[])", "int[]").WithLocation(3, 44));
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Opposite_Explicit(LanguageVersion langVersion)
    {
        var source = """
            static class C
            {
                static void M(System.Span<int> arg) => ((int[])arg).E();
                static void E(this int[] arg) { }
            }
            """;
        CreateCompilationWithSpan(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion)).VerifyDiagnostics(
            // (3,45): error CS0030: Cannot convert type 'System.Span<int>' to 'int[]'
            //     static void M(System.Span<int> arg) => ((int[])arg).E();
            Diagnostic(ErrorCode.ERR_NoExplicitConv, "(int[])arg").WithArguments("System.Span<int>", "int[]").WithLocation(3, 45));
    }

    [Theory, MemberData(nameof(LangVersions))]
    public void Conversion_Array_Span_ExtensionMethodReceiver_Opposite_Explicit_UserDefined(LanguageVersion langVersion)
    {
        var source = """
            static class C
            {
                static void M(System.Span<int> arg) => ((int[])arg).E();
                static void E(this int[] arg) { }
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
              // Code size       12 (0xc)
              .maxstack  1
              IL_0000:  ldarg.0
              IL_0001:  call       "int[] System.Span<int>.op_Explicit(System.Span<int>)"
              IL_0006:  call       "void C.E(int[])"
              IL_000b:  ret
            }
            """);
    }
}
