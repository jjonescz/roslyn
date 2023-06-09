// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.CodeGen;

public class CodeGenRefReadonlyParameterTests : CSharpTestBase
{
    private const string RequiresLocationAttributeName = "RequiresLocationAttribute";
    private const string RequiresLocationAttributeNamespace = "System.Runtime.CompilerServices";
    private const string RequiresLocationAttributeQualifiedName = $"{RequiresLocationAttributeNamespace}.{RequiresLocationAttributeName}";

    private static void VerifyRequiresLocationAttributeSynthesized(ModuleSymbol module)
    {
        var attributeType = module.GlobalNamespace.GetMember<NamedTypeSymbol>(RequiresLocationAttributeQualifiedName);
        if (module is SourceModuleSymbol)
        {
            Assert.Null(attributeType);
        }
        else
        {
            Assert.NotNull(attributeType);
        }

        [Fact]
        public void RefReadonlyParameter_InArgument()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x = 111;
                        M(in x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  .locals init (int V_0) //x
                  // sequence point: int x = 111;
                  IL_0000:  ldc.i4.s   111
                  IL_0002:  stloc.0
                  // sequence point: M(in x);
                  IL_0003:  ldloca.s   V_0
                  IL_0005:  call       "void C.M(ref readonly int)"
                  // sequence point: }
                  IL_000a:  ret
                }
                """);
        }

        [Fact]
        public void RefReadonlyParameter_RefArgument()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x = 111;
                        M(ref x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  .locals init (int V_0) //x
                  // sequence point: int x = 111;
                  IL_0000:  ldc.i4.s   111
                  IL_0002:  stloc.0
                  // sequence point: M(ref x);
                  IL_0003:  ldloca.s   V_0
                  IL_0005:  call       "void C.M(ref readonly int)"
                  // sequence point: }
                  IL_000a:  ret
                }
                """);
        }

        [Fact]
        public void RefReadonlyParameter_PlainArgument()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x = 111;
                        M(x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics(
                // (7,11): warning CS9503: Argument 1 should be passed with 'ref' or 'in' keyword
                //         M(x);
                Diagnostic(ErrorCode.WRN_ArgExpectedRefOrIn, "x").WithArguments("1").WithLocation(7, 11));
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  .locals init (int V_0) //x
                  // sequence point: int x = 111;
                  IL_0000:  ldc.i4.s   111
                  IL_0002:  stloc.0
                  // sequence point: M(x);
                  IL_0003:  ldloca.s   V_0
                  IL_0005:  call       "void C.M(ref readonly int)"
                  // sequence point: }
                  IL_000a:  ret
                }
                """);
        }

        [Fact]
        public void Invocation_VirtualMethod()
        {
            var source = """
                class C
                {
                    protected virtual void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x = 111;
                        new C().M(ref x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       16 (0x10)
                  .maxstack  2
                  .locals init (int V_0) //x
                  // sequence point: int x = 111;
                  IL_0000:  ldc.i4.s   111
                  IL_0002:  stloc.0
                  // sequence point: new C().M(ref x);
                  IL_0003:  newobj     "C..ctor()"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  callvirt   "void C.M(ref readonly int)"
                  // sequence point: }
                  IL_000f:  ret
                }
                """);
        }

        [Fact]
        public void Invocation_Constructor()
        {
            var source = """
                class C
                {
                    C(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        int x = 111;
                        new C(ref x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       12 (0xc)
                  .maxstack  1
                  .locals init (int V_0) //x
                  // sequence point: int x = 111;
                  IL_0000:  ldc.i4.s   111
                  IL_0002:  stloc.0
                  // sequence point: new C(ref x);
                  IL_0003:  ldloca.s   V_0
                  IL_0005:  newobj     "C..ctor(ref readonly int)"
                  IL_000a:  pop
                  // sequence point: }
                  IL_000b:  ret
                }
                """);
        }

        [Fact]
        public void Invocation_FunctionPointer()
        {
            var source = """
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static unsafe void Main()
                    {
                        delegate*<ref readonly int, void> f = &M;
                        int x = 111;
                        f(ref x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111", options: TestOptions.UnsafeReleaseExe, verify: Verification.Fails);
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       19 (0x13)
                  .maxstack  2
                  .locals init (int V_0, //x
                                delegate*<ref readonly int, void> V_1)
                  // sequence point: delegate*<ref readonly int, void> f = &M;
                  IL_0000:  ldftn      "void C.M(ref readonly int)"
                  // sequence point: int x = 111;
                  IL_0006:  ldc.i4.s   111
                  IL_0008:  stloc.0
                  // sequence point: f(ref x);
                  IL_0009:  stloc.1
                  IL_000a:  ldloca.s   V_0
                  IL_000c:  ldloc.1
                  IL_000d:  calli      "delegate*<ref readonly int, void>"
                  // sequence point: }
                  IL_0012:  ret
                }
                """);
        }

        [Fact]
        public void Invocation_Delegate()
        {
            var source = """
                delegate void D(ref readonly int p);
                class C
                {
                    static void M(ref readonly int p) => System.Console.WriteLine(p);
                    static void Main()
                    {
                        D d = M;
                        int x = 111;
                        d(ref x);
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: "111");
            verifier.VerifyDiagnostics();
            verifier.VerifyMethodBody("C.Main", """
                {
                  // Code size       38 (0x26)
                  .maxstack  2
                  .locals init (int V_0) //x
                  // sequence point: D d = M;
                  IL_0000:  ldsfld     "D C.<>O.<0>__M"
                  IL_0005:  dup
                  IL_0006:  brtrue.s   IL_001b
                  IL_0008:  pop
                  IL_0009:  ldnull
                  IL_000a:  ldftn      "void C.M(ref readonly int)"
                  IL_0010:  newobj     "D..ctor(object, System.IntPtr)"
                  IL_0015:  dup
                  IL_0016:  stsfld     "D C.<>O.<0>__M"
                  // sequence point: int x = 111;
                  IL_001b:  ldc.i4.s   111
                  IL_001d:  stloc.0
                  // sequence point: d(ref x);
                  IL_001e:  ldloca.s   V_0
                  IL_0020:  callvirt   "void D.Invoke(ref readonly int)"
                  // sequence point: }
                  IL_0025:  ret
                }
                """);
        }
    }

    [Fact]
    public void Method()
    {
        var source = """
            class C
            {
                public void M(ref readonly int p) { }
            }
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", "M", """
            .method public hidebysig 
            	instance void M (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method C::M
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void ManuallyDefinedAttribute()
    {
        var source = $$"""
            class C
            {
                public void M(ref readonly int p) { }
            }

            namespace {{RequiresLocationAttributeNamespace}}
            {
                class {{RequiresLocationAttributeName}}
                {
                }
            }
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            Assert.NotNull(m.GlobalNamespace.GetMember<NamedTypeSymbol>(RequiresLocationAttributeQualifiedName));
        }
    }

    [Fact]
    public void Method_Virtual()
    {
        var source = """
            class C
            {
                public virtual void M(ref readonly int p) { }
            }
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", "M", """
            .method public hidebysig newslot virtual 
            	instance void M (
            		[in] int32& modreq([netstandard]System.Runtime.InteropServices.InAttribute) p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method C::M
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void Constructor()
    {
        var source = """
            class C
            {
                public C(ref readonly int p) { }
            }
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", ".ctor", """
            .method public hidebysig specialname rtspecialname 
            	instance void .ctor (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 7 (0x7)
            	.maxstack 8
            	IL_0000: ldarg.0
            	IL_0001: call instance void [netstandard]System.Object::.ctor()
            	IL_0006: ret
            } // end of method C::.ctor
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C..ctor").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void PrimaryConstructor_Class()
    {
        var source = """
            class C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics(
            // (1,26): warning CS9113: Parameter 'p' is unread.
            // class C(ref readonly int p);
            Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "p").WithArguments("p").WithLocation(1, 26));
        verifier.VerifyMethodIL("C", ".ctor", """
            .method public hidebysig specialname rtspecialname 
            	instance void .ctor (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 7 (0x7)
            	.maxstack 8
            	IL_0000: ldarg.0
            	IL_0001: call instance void [netstandard]System.Object::.ctor()
            	IL_0006: ret
            } // end of method C::.ctor
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C..ctor").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void PrimaryConstructor_Struct()
    {
        var source = """
            struct C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics(
            // (1,27): warning CS9113: Parameter 'p' is unread.
            // struct C(ref readonly int p);
            Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "p").WithArguments("p").WithLocation(1, 27));
        verifier.VerifyMethodIL("C", ".ctor", """
            .method public hidebysig specialname rtspecialname 
            	instance void .ctor (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method C::.ctor
            """);

        static void verify(ModuleSymbol m)
        {
            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void PrimaryConstructor_Record()
    {
        var source = """
            record C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(new[] { source, IsExternalInitTypeDefinition }, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", ".ctor", """
            .method public hidebysig specialname rtspecialname 
            	instance void .ctor (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x209d
            	// Code size 15 (0xf)
            	.maxstack 8
            	IL_0000: ldarg.0
            	IL_0001: ldarg.1
            	IL_0002: ldind.i4
            	IL_0003: stfld int32 C::'<p>k__BackingField'
            	IL_0008: ldarg.0
            	IL_0009: call instance void [netstandard]System.Object::.ctor()
            	IL_000e: ret
            } // end of method C::.ctor
            """);

        static void verify(ModuleSymbol m)
        {
            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void PrimaryConstructor_RecordStruct()
    {
        var source = """
            record struct C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(new[] { source, IsExternalInitTypeDefinition }, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", ".ctor", """
            .method public hidebysig specialname rtspecialname 
            	instance void .ctor (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2067
            	// Code size 9 (0x9)
            	.maxstack 8
            	IL_0000: ldarg.0
            	IL_0001: ldarg.1
            	IL_0002: ldind.i4
            	IL_0003: stfld int32 C::'<p>k__BackingField'
            	IL_0008: ret
            } // end of method C::.ctor
            """);

        static void verify(ModuleSymbol m)
        {
            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void Delegate()
    {
        var source = """
            delegate void D(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();
        verifier.VerifyTypeIL("D", """
            .class private auto ansi sealed D
                extends [netstandard]System.MulticastDelegate
            {
                // Methods
                .method public hidebysig specialname rtspecialname 
                	instance void .ctor (
                		object 'object',
                		native int 'method'
                	) runtime managed 
                {
                } // end of method D::.ctor
                .method public hidebysig newslot virtual 
                	instance void Invoke (
                		[in] int32& modreq([netstandard]System.Runtime.InteropServices.InAttribute) p
                	) runtime managed 
                {
                	.param [1]
                		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                			01 00 00 00
                		)
                } // end of method D::Invoke
                .method public hidebysig newslot virtual 
                	instance class [netstandard]System.IAsyncResult BeginInvoke (
                		[in] int32& modreq([netstandard]System.Runtime.InteropServices.InAttribute) p,
                		class [netstandard]System.AsyncCallback callback,
                		object 'object'
                	) runtime managed 
                {
                	.param [1]
                		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                			01 00 00 00
                		)
                } // end of method D::BeginInvoke
                .method public hidebysig newslot virtual 
                	instance void EndInvoke (
                		[in] int32& modreq([netstandard]System.Runtime.InteropServices.InAttribute) p,
                		class [netstandard]System.IAsyncResult result
                	) runtime managed 
                {
                	.param [1]
                		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                			01 00 00 00
                		)
                } // end of method D::EndInvoke
            } // end of class D
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("D.Invoke").Parameters.Single();
            Assert.Equal(RefKind.RefReadOnlyParameter, p.RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void Lambda()
    {
        var source = """
            var lam = (ref readonly int p) => { };
            System.Console.WriteLine(lam.GetType());
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: VerifyRequiresLocationAttributeSynthesized, symbolValidator: VerifyRequiresLocationAttributeSynthesized,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("<>c", "<<Main>$>b__0_0", """
            .method assembly hidebysig 
            	instance void '<<Main>$>b__0_0' (
            		[in] int32& p
            	) cil managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x20a6
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method '<>c'::'<<Main>$>b__0_0'
            """);
    }

    [Fact]
    public void LocalFunction()
    {
        var source = """
            void local(ref readonly int p) { }
            System.Console.WriteLine(((object)local).GetType());
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: VerifyRequiresLocationAttributeSynthesized, symbolValidator: VerifyRequiresLocationAttributeSynthesized,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("Program", "<<Main>$>g__local|0_0", """
            .method assembly hidebysig static 
            	void '<<Main>$>g__local|0_0' (
            		[in] int32& p
            	) cil managed 
            {
            	.custom instance void [netstandard]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
            		01 00 00 00
            	)
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            	// Method begins at RVA 0x2087
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method Program::'<<Main>$>g__local|0_0'
            """);
    }

    [Theory]
    [InlineData("var x = (ref readonly int p) => { };")]
    [InlineData("var x = local; void local(ref readonly int p) { }")]
    public void AnonymousDelegate(string def)
    {
        var source = $"""
            {def}
            System.Console.WriteLine(((object)x).GetType());
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: VerifyRequiresLocationAttributeSynthesized, symbolValidator: VerifyRequiresLocationAttributeSynthesized,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("<>f__AnonymousDelegate0`1", "Invoke", """
            .method public hidebysig newslot virtual 
            	instance void Invoke (
            		[in] !T1& arg
            	) runtime managed 
            {
            	.param [1]
            		.custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
            			01 00 00 00
            		)
            } // end of method '<>f__AnonymousDelegate0`1'::Invoke
            """);
    }

    [Fact]
    public void FunctionPointer()
    {
        var source = """
            class C
            {
                public unsafe void M(delegate*<ref readonly int, void> p) { }
            }
            """;
        var verifier = CompileAndVerify(source, options: TestOptions.UnsafeReleaseDll, targetFramework: TargetFramework.NetStandard20,
            sourceSymbolValidator: verify); // PROTOTYPE:, symbolValidator: verify
        verifier.VerifyDiagnostics();
        verifier.VerifyMethodIL("C", "M", """
            .method public hidebysig 
            	instance void M (
            		method void *(int32& modreq([netstandard]System.Runtime.InteropServices.InAttribute)) p
            	) cil managed 
            {
            	// Method begins at RVA 0x2067
            	// Code size 1 (0x1)
            	.maxstack 8
            	IL_0000: ret
            } // end of method C::M
            """);

        static void verify(ModuleSymbol m)
        {
            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            var ptr = (FunctionPointerTypeSymbol)p.Type;
            Assert.Equal(RefKind.RefReadOnlyParameter, ptr.Signature.Parameters.Single().RefKind);
            VerifyRequiresLocationAttributeSynthesized(m);
        }
    }

    [Fact]
    public void AttributeIL()
    {
        var source = """
            class C
            {
                public void M(ref readonly int p) { }
            }
            """;
        var verifier = CompileAndVerify(source, targetFramework: TargetFramework.NetStandard20);
        verifier.VerifyDiagnostics();
        verifier.VerifyTypeIL(RequiresLocationAttributeName, """
            .class private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RequiresLocationAttribute
                extends [netstandard]System.Attribute
            {
                .custom instance void [netstandard]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
                	01 00 00 00
                )
                .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
                	01 00 00 00
                )
                // Methods
                .method public hidebysig specialname rtspecialname 
                	instance void .ctor () cil managed 
                {
                	// Method begins at RVA 0x2050
                	// Code size 7 (0x7)
                	.maxstack 8
                	IL_0000: ldarg.0
                	IL_0001: call instance void [netstandard]System.Attribute::.ctor()
                	IL_0006: ret
                } // end of method RequiresLocationAttribute::.ctor
            } // end of class System.Runtime.CompilerServices.RequiresLocationAttribute
            """);
    }
}
