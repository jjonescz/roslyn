// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Semantics;

// PROTOTYPE: Move to RefReadonlyParameterTests.cs.
public partial class RefReadonlyParameterTests : CSharpTestBase
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
    }

    private static void VerifyRefReadonlyParameter(ParameterSymbol parameter,
        bool refKind = true,
        bool metadataIn = true,
        bool attributes = true,
        bool modreq = false,
        bool useSiteError = false)
    {
        Assert.Equal(refKind, RefKind.RefReadOnlyParameter == parameter.RefKind);

        Assert.Equal(metadataIn, parameter.IsMetadataIn);

        if (attributes)
        {
            if (parameter.ContainingModule is SourceModuleSymbol)
            {
                Assert.Empty(parameter.GetAttributes());
            }
            else
            {
                var attribute = Assert.Single(parameter.GetAttributes());
                Assert.Equal("System.Runtime.CompilerServices.RequiresLocationAttribute", attribute.AttributeClass.ToTestDisplayString());
                Assert.Empty(attribute.ConstructorArguments);
                Assert.Empty(attribute.NamedArguments);
            }
        }

        if (modreq)
        {
            var mod = Assert.Single(parameter.RefCustomModifiers);
            Assert.Equal("System.Runtime.InteropServices.InAttribute", mod.Modifier.ToTestDisplayString());
        }
        else
        {
            Assert.Empty(parameter.RefCustomModifiers);
        }

        var method = (MethodSymbol)parameter.ContainingSymbol;

        if (useSiteError)
        {
            Assert.True(method.HasUnsupportedMetadata);
            Assert.True(method.HasUseSiteError);
            Assert.Equal((int)ErrorCode.ERR_BindToBogus, method.GetUseSiteDiagnostic().Code);
        }
        else
        {
            Assert.False(method.HasUnsupportedMetadata);
            Assert.False(method.HasUseSiteError);
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
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            VerifyRefReadonlyParameter(p);
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
                class {{RequiresLocationAttributeName}} : System.Attribute
                {
                }
            }
            """;
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            var attribute = m.GlobalNamespace.GetMember<NamedTypeSymbol>(RequiresLocationAttributeQualifiedName);
            Assert.NotNull(attribute);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            VerifyRefReadonlyParameter(p);

            if (m is not SourceModuleSymbol)
            {
                Assert.Same(attribute, p.GetAttributes().Single().AttributeClass);
            }
        }
    }

    [Fact]
    public void BothAttributes()
    {
        // public class C
        // {
        //     public void M([IsReadOnly] ref readonly int p) { }
        // }
        var ilSource = """
            .class public auto ansi abstract sealed beforefieldinit C extends System.Object
            {
                .method public hidebysig instance void M([in] int32& p) cil managed
                {
                    .param [1]
                        .custom instance void System.Runtime.CompilerServices.IsReadOnlyAttribute::.ctor() = (
                            01 00 00 00
                        )
                        .custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                            01 00 00 00
                        )
                    .maxstack 8
                    ret
                }
            }

            .class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.IsReadOnlyAttribute extends System.Object
            {
                .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
                {
                    .maxstack 8
                    ret
                }
            }
            
            .class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RequiresLocationAttribute extends System.Object
            {
                .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
                {
                    .maxstack 8
                    ret
                }
            }
            """;
        var comp = CreateCompilationWithIL("", ilSource).VerifyDiagnostics();

        var p = comp.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
        VerifyRefReadonlyParameter(p, attributes: false);
        var attributes = p.GetAttributes();
        Assert.Equal(new[]
        {
            "System.Runtime.CompilerServices.IsReadOnlyAttribute",
            "System.Runtime.CompilerServices.RequiresLocationAttribute"
        }, attributes.Select(a => a.AttributeClass.ToTestDisplayString()));
        Assert.All(attributes, a =>
        {
            Assert.Empty(a.ConstructorArguments);
            Assert.Empty(a.NamedArguments);
        });
    }

    [Fact]
    public void ReturnParameter()
    {
        // public class C
        // {
        //     [return: RequiresLocation]
        //     public ref int M() { }
        // }
        var ilSource = """
            .class public auto ansi abstract sealed beforefieldinit C extends System.Object
            {
                .method public hidebysig instance int32& M() cil managed
                {
                    .param [0]
                        .custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                            01 00 00 00
                        )
                    .maxstack 8
                    ret
                }
            }
            
            .class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RequiresLocationAttribute extends System.Object
            {
                .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
                {
                    .maxstack 8
                    ret
                }
            }
            """;
        var comp = CreateCompilationWithIL("", ilSource).VerifyDiagnostics();

        var m = comp.GlobalNamespace.GetMember<MethodSymbol>("C.M");
        Assert.Equal(RefKind.Ref, m.RefKind);
    }

    [Fact]
    public void Modreq_NonVirtual()
    {
        // public class C
        // {
        //     public void M(modreq(In) ref readonly int p) { }
        // }
        var ilSource = """
            .class public auto ansi abstract sealed beforefieldinit C extends System.Object
            {
                .method public hidebysig instance void M(
                    [in] int32& modreq(System.Runtime.InteropServices.InAttribute) p
                    ) cil managed
                {
                    .param [1]
                        .custom instance void System.Runtime.CompilerServices.RequiresLocationAttribute::.ctor() = (
                            01 00 00 00
                        )
                    .maxstack 8
                    ret
                }
            }
            
            .class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RequiresLocationAttribute extends System.Object
            {
                .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
                {
                    .maxstack 8
                    ret
                }
            }
            
            .class public auto ansi sealed beforefieldinit System.Runtime.InteropServices.InAttribute extends System.Object
            {
                .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
                {
                    .maxstack 8
                    ret
                }
            }
            """;
        var comp = CreateCompilationWithIL("", ilSource).VerifyDiagnostics();

        var p = comp.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
        VerifyRefReadonlyParameter(p, modreq: true, useSiteError: true);
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
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            VerifyRefReadonlyParameter(p, modreq: true);
        }
    }

    [Fact]
    public void Method_Abstract()
    {
        var source = """
            abstract class C
            {
                public abstract void M(ref readonly int p);
            }
            """;
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            VerifyRefReadonlyParameter(p, modreq: true);
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
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C..ctor").Parameters.Single();
            VerifyRefReadonlyParameter(p);
        }
    }

    [Fact]
    public void PrimaryConstructor_Class()
    {
        var source = """
            class C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics(
            // (1,26): warning CS9113: Parameter 'p' is unread.
            // class C(ref readonly int p);
            Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "p").WithArguments("p").WithLocation(1, 26));

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C..ctor").Parameters.Single();
            VerifyRefReadonlyParameter(p);
        }
    }

    [Fact]
    public void PrimaryConstructor_Struct()
    {
        var source = """
            struct C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics(
            // (1,27): warning CS9113: Parameter 'p' is unread.
            // struct C(ref readonly int p);
            Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "p").WithArguments("p").WithLocation(1, 27));

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            VerifyRefReadonlyParameter(p);
        }
    }

    [Fact]
    public void PrimaryConstructor_Record()
    {
        var source = """
            record C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(new[] { source, IsExternalInitTypeDefinition },
            sourceSymbolValidator: verify, symbolValidator: verify,
            verify: Verification.FailsPEVerify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            VerifyRefReadonlyParameter(p);
        }
    }

    [Fact]
    public void PrimaryConstructor_RecordStruct()
    {
        var source = """
            record struct C(ref readonly int p);
            """;
        var verifier = CompileAndVerify(new[] { source, IsExternalInitTypeDefinition },
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var c = m.GlobalNamespace.GetTypeMember("C");
            var ctor = c.InstanceConstructors.Single(s => s.Parameters is [{ Name: "p" }]);
            var p = ctor.Parameters.Single();
            VerifyRefReadonlyParameter(p);
        }
    }

    [Fact]
    public void Delegate()
    {
        var source = """
            delegate void D(ref readonly int p);
            """;
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("D.Invoke").Parameters.Single();
            VerifyRefReadonlyParameter(p, modreq: true);
        }
    }

    [Fact]
    public void Lambda()
    {
        var source = """
            var lam = (ref readonly int p) => { };
            System.Console.WriteLine(lam.GetType());
            """;
        var verifier = CompileAndVerify(source, options: TestOptions.DebugExe.WithMetadataImportOptions(MetadataImportOptions.All),
            sourceSymbolValidator: verify, symbolValidator: verify,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            if (m is not SourceModuleSymbol)
            {
                var p = m.GlobalNamespace.GetMember<MethodSymbol>("Program.<>c.<<Main>$>b__0_0").Parameters.Single();
                VerifyRefReadonlyParameter(p);
            }
        }
    }

    [Fact]
    public void LocalFunction()
    {
        var source = """
            void local(ref readonly int p) { }
            System.Console.WriteLine(((object)local).GetType());
            """;
        var verifier = CompileAndVerify(source, options: TestOptions.DebugExe.WithMetadataImportOptions(MetadataImportOptions.All),
            sourceSymbolValidator: verify, symbolValidator: verify,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            if (m is not SourceModuleSymbol)
            {
                var p = m.GlobalNamespace.GetMember<MethodSymbol>("Program.<<Main>$>g__local|0_0").Parameters.Single();
                VerifyRefReadonlyParameter(p);
            }
        }
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
        var verifier = CompileAndVerify(source, sourceSymbolValidator: verify, symbolValidator: verify,
            expectedOutput: "<>f__AnonymousDelegate0`1[System.Int32]");
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            VerifyRequiresLocationAttributeSynthesized(m);

            if (m is not SourceModuleSymbol)
            {
                var p = m.GlobalNamespace.GetMember<MethodSymbol>("<>f__AnonymousDelegate0.Invoke").Parameters.Single();
                VerifyRefReadonlyParameter(p,
                    // PROTOTYPE: Invoke method is virtual but no modreq is emitted. This happens for `in` parameters, as well.
                    useSiteError: true);
            }
        }
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
        var verifier = CompileAndVerify(source, options: TestOptions.UnsafeReleaseDll,
            sourceSymbolValidator: verify, symbolValidator: verify);
        verifier.VerifyDiagnostics();

        static void verify(ModuleSymbol m)
        {
            Assert.Null(m.GlobalNamespace.GetMember<NamedTypeSymbol>(RequiresLocationAttributeQualifiedName));

            var p = m.GlobalNamespace.GetMember<MethodSymbol>("C.M").Parameters.Single();
            var ptr = (FunctionPointerTypeSymbol)p.Type;
            var p2 = ptr.Signature.Parameters.Single();
            VerifyRefReadonlyParameter(p2, refKind: m is SourceModuleSymbol, modreq: true, attributes: false);
            Assert.Equal(m is SourceModuleSymbol ? RefKind.RefReadOnlyParameter : RefKind.In, p2.RefKind);
            Assert.Empty(p2.GetAttributes());
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
