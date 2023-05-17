// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.Semantics
{
    public class RefReadOnlyParameterTests : CSharpTestBase
    {
        [Fact]
        public void RefReadonlyParameter_OutArgument()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x;
                        M(out x);
                    }
                }
                """;
            CreateCompilation(source).VerifyDiagnostics(
                // (7,15): error CS1620: Argument 1 must be passed with the 'ref readonly' keyword
                //         M(out x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref readonly").WithLocation(7, 15));
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_CrossAssembly()
        {
            var source1 = """
                public class C
                {
                    public void M(ref readonly int p) { }
                    void M2()
                    {
                        int x = 5;
                        M(ref x);
                    }
                }
                """;
            var comp1 = CreateCompilation(source1).VerifyDiagnostics();

            var source2 = """
                class D
                {
                    void M(C c)
                    {
                        int x = 6;
                        c.M(ref x);
                    }
                }
                """;
            CreateCompilation(source2, new[] { comp1.ToMetadataReference() }, parseOptions: TestOptions.Regular11).VerifyDiagnostics();
        }

        [Fact]
        public void RefReadonlyParameter_InArgument_CrossAssembly()
        {
            var source1 = """
                public class C
                {
                    public void M(ref readonly int p) { }
                    void M2()
                    {
                        int x = 5;
                        M(in x);
                    }
                }
                """;
            var comp1 = CreateCompilation(source1).VerifyDiagnostics();

            var source2 = """
                class D
                {
                    void M(C c)
                    {
                        int x = 6;
                        c.M(in x);
                    }
                }
                """;
            CreateCompilation(source2, new[] { comp1.ToMetadataReference() }, parseOptions: TestOptions.Regular11).VerifyDiagnostics();
        }

        [Fact]
        public void RefReadonlyParameter_PlainArgument_CrossAssembly()
        {
            var source1 = """
                public class C
                {
                    public void M(ref readonly int p) { }
                    void M2()
                    {
                        int x = 5;
                        M(x);
                    }
                }
                """;
            var comp1 = CreateCompilation(source1).VerifyDiagnostics(
                // (7,11): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         M(x);
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(7, 11));

            var source2 = """
                class D
                {
                    void M(C c)
                    {
                        int x = 6;
                        c.M(x);
                    }
                }
                """;
            CreateCompilation(source2, new[] { comp1.ToMetadataReference() }, parseOptions: TestOptions.Regular11).VerifyDiagnostics(
                // (6,13): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         c.M(x);
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(6, 13));
        }

        [Fact]
        public void RefReadonlyParameter_PlainArgument_Ctor()
        {
            var source = """
                class C
                {
                    private C(ref readonly int p)
                    {
                        System.Console.WriteLine(p);
                    }

                    static void Main()
                    {
                        int x = 5;
                        new C(x);
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: "5").VerifyDiagnostics(
                // (11,15): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         new C(x);
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(11, 15));
        }

        [Fact]
        public void RefReadonlyParameter_FunctionPointer()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.Write(p);
                    static unsafe void Main()
                    {
                        delegate*<ref readonly int, void> f = &M;
                        int x = 5;
                        f(x);
                        f(ref x);
                        f(in x);
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: "555", options: TestOptions.UnsafeReleaseExe, verify: Verification.Fails).VerifyDiagnostics(
                // (8,11): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         f(x);
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(8, 11));
        }

        [Fact]
        public void RefReadonlyParameter_FunctionPointer_OutArgument()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.Write(p);
                    static unsafe void Main()
                    {
                        delegate*<ref readonly int, void> f = &M;
                        int x = 5;
                        f(out x);
                    }
                }
                """;
            CreateCompilation(source, options: TestOptions.UnsafeReleaseExe).VerifyDiagnostics(
                // (8,15): error CS1620: Argument 1 must be passed with the 'ref readonly' keyword
                //         f(out x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref readonly").WithLocation(8, 15));
        }

        [Fact]
        public void RefReadonlyParameter_NamedArguments()
        {
            var source = """
                class C
                {
                    static void M(in int a, ref readonly int b)
                    {
                        System.Console.Write(a);
                        System.Console.Write(b);
                    }
                    static void Main()
                    {
                        int x = 5;
                        int y = 6;
                        M(b: x, a: y); // 1
                        M(b: x, a: in y); // 2
                        M(a: x, y); // 3
                        M(a: x, in y); // 4
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: "65655656").VerifyDiagnostics(
                // (12,14): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         M(b: x, a: y); // 1
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(12, 14),
                // (13,14): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         M(b: x, a: in y); // 2
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(13, 14),
                // (14,17): warning CS9503: Argument 2 should be passed with 'ref' or 'in' keyword
                //         M(a: x, y); // 3
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "y").WithArguments("2").WithLocation(14, 17));
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_OverloadResolution_01()
        {
            var source = """
                class C
                {
                    static string M1(string s, ref int i) => "string" + i;
                    static string M1(object o, in int i) => "object" + i;
                    static string M1(C c, ref readonly int i) => "c" + i;
                    static void Main()
                    {
                        int i = 5;
                        System.Console.WriteLine(M1(null, ref i));
                    }
                }
                """;
            CreateCompilation(source).VerifyDiagnostics(
                // (9,34): error CS0121: The call is ambiguous between the following methods or properties: 'C.M1(string, ref int)' and 'C.M1(C, ref readonly int)'
                //         System.Console.WriteLine(M1(null, ref i));
                Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("C.M1(string, ref int)", "C.M1(C, ref readonly int)").WithLocation(9, 34));
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_OverloadResolution_01_Ctor()
        {
            var source = """
                class C
                {
                    private C(string s, ref int i) => System.Console.WriteLine("string" + i);
                    private C(object o, in int i) => System.Console.WriteLine("object" + i);
                    private C(C c, ref readonly int i) => System.Console.WriteLine("c" + i);
                    static void Main()
                    {
                        int i = 5;
                        new C(null, ref i);
                    }
                }
                """;
            CreateCompilation(source).VerifyDiagnostics(
                // (9,13): error CS0121: The call is ambiguous between the following methods or properties: 'C.C(string, ref int)' and 'C.C(C, ref readonly int)'
                //         new C(null, ref i);
                Diagnostic(ErrorCode.ERR_AmbigCall, "C").WithArguments("C.C(string, ref int)", "C.C(C, ref readonly int)").WithLocation(9, 13));
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_OverloadResolution_02()
        {
            var source = """
                class C
                {
                    static string M1(string s, ref int i) => "string" + i;
                    static string M1(object o, in int i) => "object" + i;
                    static string M1(C c, ref readonly int i) => "c" + i;
                    static void Main()
                    {
                        int i = 5;
                        System.Console.WriteLine(M1(default(string), ref i));
                        System.Console.WriteLine(M1(default(object), ref i));
                        System.Console.WriteLine(M1(default(C), ref i));
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: """
                string5
                object5
                c5
                """).VerifyDiagnostics(
                // (10,58): warning CS9502: Argument 2 should not be passed with the 'ref' keyword
                //         System.Console.WriteLine(M1(default(object), ref i));
                Diagnostic(ErrorCode.WRN_BadArgRef, "i").WithArguments("2", "ref").WithLocation(10, 58));
        }

        [Fact]
        public void PassingArgumentsToInParameters_RefKind_Ref_02_Ctor()
        {
            var source = """
                class C
                {
                    private C(string s, ref int i) => System.Console.WriteLine("string" + i);
                    private C(object o, in int i) => System.Console.WriteLine("object" + i);
                    static void Main()
                    {
                        int i = 5;
                        new C(default(object), ref i);
                    }
                }
                """;
            CreateCompilation(source, parseOptions: TestOptions.Regular11).VerifyDiagnostics(
                // (8,15): error CS1503: Argument 1: cannot convert from 'object' to 'string'
                //         new C(default(object), ref i);
                Diagnostic(ErrorCode.ERR_BadArgType, "default(object)").WithArguments("1", "object", "string").WithLocation(8, 15));

            var expectedDiagnostics = new[]
            {
                // (8,36): warning CS9501: Argument 2 should not be passed with the 'ref' keyword
                //         new C(default(object), ref i);
                Diagnostic(ErrorCode.WRN_BadArgRef, "i").WithArguments("2", "ref").WithLocation(8, 36)
            };

            CompileAndVerify(source, expectedOutput: "object5", parseOptions: TestOptions.RegularNext).VerifyDiagnostics(expectedDiagnostics);
            CompileAndVerify(source, expectedOutput: "object5").VerifyDiagnostics(expectedDiagnostics);
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_OverloadResolution_03()
        {
            var source = """
                class C
                {
                    static string M1(object o, in int i) => "object" + i;
                    static string M1(C c, ref readonly int i) => "c" + i;
                    static void Main()
                    {
                        int i = 5;
                        System.Console.WriteLine(M1(null, ref i));
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: "c5").VerifyDiagnostics();
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument_OverloadResolution_03_Ctor()
        {
            var source = """
                class C
                {
                    private C(object o, in int i) => System.Console.WriteLine("object" + i);
                    private C(C c, ref readonly int i) => System.Console.WriteLine("c" + i);
                    static void Main()
                    {
                        int i = 5;
                        new C(null, ref i);
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: "c5").VerifyDiagnostics();
        }
    }
}
