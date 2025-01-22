// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests;

public sealed class PartialEventsAndConstructorsTests : CSharpTestBase
{
    [Fact]
    public void LangVersion()
    {
        var source = """
            partial class C
            {
                partial event System.Action E;
                partial event System.Action E { add { } remove { } }
                partial C();
                partial C() { }
            }
            """;

        CreateCompilation(source, parseOptions: TestOptions.Regular13).VerifyDiagnostics(
            // (3,33): error CS8703: The modifier 'partial' is not valid for this item in C# 13.0. Please use language version 'preview' or greater.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_InvalidModifierForLanguageVersion, "E").WithArguments("partial", "13.0", "preview").WithLocation(3, 33),
            // (4,33): error CS8703: The modifier 'partial' is not valid for this item in C# 13.0. Please use language version 'preview' or greater.
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_InvalidModifierForLanguageVersion, "E").WithArguments("partial", "13.0", "preview").WithLocation(4, 33),
            // (5,13): error CS8703: The modifier 'partial' is not valid for this item in C# 13.0. Please use language version 'preview' or greater.
            //     partial C();
            Diagnostic(ErrorCode.ERR_InvalidModifierForLanguageVersion, "C").WithArguments("partial", "13.0", "preview").WithLocation(5, 13),
            // (6,13): error CS8703: The modifier 'partial' is not valid for this item in C# 13.0. Please use language version 'preview' or greater.
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_InvalidModifierForLanguageVersion, "C").WithArguments("partial", "13.0", "preview").WithLocation(6, 13));

        CreateCompilation(source, parseOptions: TestOptions.RegularNext).VerifyDiagnostics();
        CreateCompilation(source).VerifyDiagnostics();
    }

    [Theory, CombinatorialData]
    public void PartialLast([CombinatorialValues("", "public")] string modifier)
    {
        var source = $$"""
            partial class C
            {
                {{modifier}}
                partial event System.Action E;
                {{modifier}}
                partial event System.Action E { add { } remove { } }
                {{modifier}}
                partial C();
                {{modifier}}
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics();
    }

    [Fact]
    public void PartialNotLast()
    {
        var source = """
            partial class C
            {
                partial public event System.Action E;
                partial public event System.Action E { add { } remove { } }
                partial public C();
                partial public C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(3, 5),
            // (4,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(4, 5),
            // (5,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public C();
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(5, 5),
            // (6,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public C() { }
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(6, 5));
    }

    [Fact]
    public void EventInitializer_Single()
    {
        var source = """
            partial class C
            {
                partial event System.Action E = null;
                partial event System.Action E { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9404: 'C.E': partial event cannot have initializer
            //     partial event System.Action E = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,33): warning CS0414: The field 'C.E' is assigned but its value is never used
            //     partial event System.Action E = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "E").WithArguments("C.E").WithLocation(3, 33));
    }

    [Fact]
    public void EventInitializer_Multiple_01()
    {
        var source = """
            partial class C
            {
                partial event System.Action E, F = null;
                partial event System.Action E { add { } remove { } }
                partial event System.Action F { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,36): error CS9404: 'C.F': partial event cannot have initializer
            //     partial event System.Action E, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "F").WithArguments("C.F").WithLocation(3, 36),
            // (3,36): warning CS0414: The field 'C.F' is assigned but its value is never used
            //     partial event System.Action E, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "F").WithArguments("C.F").WithLocation(3, 36));
    }

    [Fact]
    public void EventInitializer_Multiple_02()
    {
        var source = """
            partial class C
            {
                partial event System.Action E = null, F = null;
                partial event System.Action E { add { } remove { } }
                partial event System.Action F { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9404: 'C.E': partial event cannot have initializer
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,33): warning CS0414: The field 'C.E' is assigned but its value is never used
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,43): error CS9404: 'C.F': partial event cannot have initializer
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "F").WithArguments("C.F").WithLocation(3, 43),
            // (3,43): warning CS0414: The field 'C.F' is assigned but its value is never used
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "F").WithArguments("C.F").WithLocation(3, 43));
    }

    [Fact]
    public void StaticPartialConstructor()
    {
        var source = """
            partial class C
            {
                static partial C();
                static partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,12): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     static partial C();
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(3, 12),
            // (4,12): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     static partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(4, 12),
            // (4,20): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     static partial C() { }
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(4, 20));
    }

    [Fact]
    public void NotInPartialType()
    {
        var source = """
            class C
            {
                partial event System.Action E;
                partial event System.Action E { add { } remove { } }
                partial event System.Action F { add { } remove { } }
                partial C();
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS0751: A partial member must be declared within a partial type
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberOnlyInPartialClass, "E").WithLocation(3, 33),
            // (5,33): error CS9401: Partial event 'C.F' must have a definition part.
            //     partial event System.Action F { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "F").WithArguments("C.F").WithLocation(5, 33),
            // (5,33): error CS0751: A partial member must be declared within a partial type
            //     partial event System.Action F { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberOnlyInPartialClass, "F").WithLocation(5, 33),
            // (6,13): error CS0751: A partial member must be declared within a partial type
            //     partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberOnlyInPartialClass, "C").WithLocation(6, 13),
            // (7,13): error CS0751: A partial member must be declared within a partial type
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMemberOnlyInPartialClass, "C").WithLocation(7, 13));
    }

    [Fact]
    public void Abstract()
    {
        var source = """
            abstract partial class C
            {
                protected abstract partial event System.Action E;
                protected abstract partial event System.Action E { add { } remove { } }
                protected abstract partial C();
                protected abstract partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,52): error CS0750: A partial member cannot have the 'abstract' modifier
            //     protected abstract partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberCannotBeAbstract, "E").WithLocation(3, 52),
            // (4,54): error CS8712: 'C.E': abstract event cannot use event accessor syntax
            //     protected abstract partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_AbstractEventHasAccessors, "{").WithArguments("C.E").WithLocation(4, 54),
            // (5,32): error CS0106: The modifier 'abstract' is not valid for this item
            //     protected abstract partial C();
            Diagnostic(ErrorCode.ERR_BadMemberFlag, "C").WithArguments("abstract").WithLocation(5, 32),
            // (6,32): error CS0106: The modifier 'abstract' is not valid for this item
            //     protected abstract partial C() { }
            Diagnostic(ErrorCode.ERR_BadMemberFlag, "C").WithArguments("abstract").WithLocation(6, 32));
    }

    [Fact]
    public void ExplicitInterfaceImplementation()
    {
        var source = """
            interface I
            {
                event System.Action E;
            }
            partial class C : I
            {
                partial event System.Action I.E;
                partial event System.Action I.E { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (5,15): error CS8646: 'I.E' is explicitly implemented more than once.
            // partial class C : I
            Diagnostic(ErrorCode.ERR_DuplicateExplicitImpl, "C").WithArguments("I.E").WithLocation(5, 15),
            // (7,35): error CS0071: An explicit interface implementation of an event must use event accessor syntax
            //     partial event System.Action I.E;
            Diagnostic(ErrorCode.ERR_ExplicitEventFieldImpl, "E").WithLocation(7, 35),
            // (7,35): error CS9401: Partial member 'C.I.E' must have a definition part.
            //     partial event System.Action I.E;
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "E").WithArguments("C.I.E").WithLocation(7, 35),
            // (7,35): error CS0754: A partial member may not explicitly implement an interface member
            //     partial event System.Action I.E;
            Diagnostic(ErrorCode.ERR_PartialMemberNotExplicit, "E").WithLocation(7, 35),
            // (8,35): error CS9403: Partial member 'C.I.E' may not have multiple implementing declarations.
            //     partial event System.Action I.E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.I.E").WithLocation(8, 35),
            // (8,35): error CS0102: The type 'C' already contains a definition for 'I.E'
            //     partial event System.Action I.E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "I.E").WithLocation(8, 35),
            // (8,35): error CS0754: A partial member may not explicitly implement an interface member
            //     partial event System.Action I.E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberNotExplicit, "E").WithLocation(8, 35));
    }
}
