// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
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
    public void PartialAsType()
    {
        var source = """
            partial class C
            {
                partial C() => new partial();
            }

            class @partial;
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,13): error CS9401: Partial member 'C.C()' must have a definition part.
            //     partial C() => new partial();
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "C").WithArguments("C.C()").WithLocation(3, 13));
    }

    [Fact]
    public void MissingImplementation()
    {
        var source = """
            partial class C
            {
                partial event System.Action E;
                partial C();
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9400: Partial member 'C.E' must have an implementation part.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberMissingImplementation, "E").WithArguments("C.E").WithLocation(3, 33),
            // (4,13): error CS9400: Partial member 'C.C()' must have an implementation part.
            //     partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberMissingImplementation, "C").WithArguments("C.C()").WithLocation(4, 13));
    }

    [Fact]
    public void MissingDefinition()
    {
        var source = """
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9401: Partial member 'C.E' must have a definition part.
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "E").WithArguments("C.E").WithLocation(3, 33),
            // (4,13): error CS9401: Partial member 'C.C()' must have a definition part.
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "C").WithArguments("C.C()").WithLocation(4, 13));
    }

    [Fact]
    public void DuplicateDefinition()
    {
        var source = """
            partial class C
            {
                partial event System.Action E, F;
                partial event System.Action E;
                partial event System.Action F;
                partial C();
                partial C();

                partial event System.Action E { add { } remove { } }
                partial event System.Action F { add { } remove { } }
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (4,33): error CS9403: Partial member 'C.E' may not have multiple implementing declarations.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.E").WithLocation(4, 33),
            // (4,33): error CS0102: The type 'C' already contains a definition for 'E'
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(4, 33),
            // (5,33): error CS9403: Partial member 'C.F' may not have multiple implementing declarations.
            //     partial event System.Action F;
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "F").WithArguments("C.F").WithLocation(5, 33),
            // (5,33): error CS0102: The type 'C' already contains a definition for 'F'
            //     partial event System.Action F;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "F").WithArguments("C", "F").WithLocation(5, 33),
            // (7,13): error CS9403: Partial member 'C.C()' may not have multiple implementing declarations.
            //     partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "C").WithArguments("C.C()").WithLocation(7, 13),
            // (7,13): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     partial C();
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(7, 13));
    }

    [Fact]
    public void DuplicateImplementation()
    {
        var source = """
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial event System.Action E { add { } remove { } }
                partial C() { }
                partial C() { }

                partial event System.Action E;
                partial C();
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (4,33): error CS9403: Partial member 'C.E' may not have multiple implementing declarations.
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.E").WithLocation(4, 33),
            // (6,13): error CS9403: Partial member 'C.C()' may not have multiple implementing declarations.
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "C").WithArguments("C.C()").WithLocation(6, 13),
            // (8,33): error CS0102: The type 'C' already contains a definition for 'E'
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(8, 33),
            // (9,13): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     partial C();
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(9, 13));
    }

    [Fact]
    public void DuplicateDeclarations_01()
    {
        var source = """
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial event System.Action E { add { } remove { } }
                partial C() { }
                partial C() { }

                partial event System.Action E;
                partial event System.Action E;
                partial C();
                partial C();
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (4,33): error CS9403: Partial member 'C.E' may not have multiple implementing declarations.
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.E").WithLocation(4, 33),
            // (6,13): error CS9403: Partial member 'C.C()' may not have multiple implementing declarations.
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "C").WithArguments("C.C()").WithLocation(6, 13),
            // (8,33): error CS0102: The type 'C' already contains a definition for 'E'
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(8, 33),
            // (9,33): error CS9403: Partial member 'C.E' may not have multiple implementing declarations.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.E").WithLocation(9, 33),
            // (9,33): error CS0102: The type 'C' already contains a definition for 'E'
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(9, 33),
            // (10,13): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     partial C();
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(10, 13),
            // (11,13): error CS9403: Partial member 'C.C()' may not have multiple implementing declarations.
            //     partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "C").WithArguments("C.C()").WithLocation(11, 13),
            // (11,13): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     partial C();
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(11, 13));
    }

    [Fact]
    public void DuplicateDeclarations_02()
    {
        var source = """
            partial class C
            {
                partial event System.Action E;
                partial void add_E(System.Action value);
                partial void remove_E(System.Action value);
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9400: Partial member 'C.E' must have an implementation part.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberMissingImplementation, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,33): error CS0082: Type 'C' already reserves a member called 'add_E' with the same parameter types
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_MemberReserved, "E").WithArguments("add_E", "C").WithLocation(3, 33),
            // (3,33): error CS0082: Type 'C' already reserves a member called 'remove_E' with the same parameter types
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_MemberReserved, "E").WithArguments("remove_E", "C").WithLocation(3, 33));
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
    public void EventAccessorMissing()
    {
        var source = """
            partial class C
            {
                partial event System.Action E, F;
                partial event System.Action E { add { } }
                partial event System.Action F { remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (4,33): error CS0065: 'C.E': event property must have both add and remove accessors
            //     partial event System.Action E { add { } }
            Diagnostic(ErrorCode.ERR_EventNeedsBothAccessors, "E").WithArguments("C.E").WithLocation(4, 33),
            // (5,33): error CS0065: 'C.F': event property must have both add and remove accessors
            //     partial event System.Action F { remove { } }
            Diagnostic(ErrorCode.ERR_EventNeedsBothAccessors, "F").WithArguments("C.F").WithLocation(5, 33));
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

    [Fact]
    public void Extern_01()
    {
        var source = """
            partial class C
            {
                partial event System.Action E;
                extern partial event System.Action E;

                partial C();
                extern partial C();
            }
            """;
        CreateCompilation(source).VerifyDiagnostics();
    }

    [Fact]
    public void Extern_02()
    {
        var source = """
            partial class C
            {
                partial event System.Action E;
                extern event System.Action E;

                partial C();
                extern C();
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9400: Partial member 'C.E' must have an implementation part.
            //     partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberMissingImplementation, "E").WithArguments("C.E").WithLocation(3, 33),
            // (4,32): error CS0102: The type 'C' already contains a definition for 'E'
            //     extern event System.Action E;
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(4, 32),
            // (4,32): warning CS0626: Method, operator, or accessor 'C.E.remove' is marked external and has no attributes on it. Consider adding a DllImport attribute to specify the external implementation.
            //     extern event System.Action E;
            Diagnostic(ErrorCode.WRN_ExternMethodNoImplementation, "E").WithArguments("C.E.remove").WithLocation(4, 32),
            // (6,13): error CS9400: Partial member 'C.C()' must have an implementation part.
            //     partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberMissingImplementation, "C").WithArguments("C.C()").WithLocation(6, 13),
            // (7,12): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     extern C();
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(7, 12),
            // (7,12): warning CS0824: Constructor 'C.C()' is marked external
            //     extern C();
            Diagnostic(ErrorCode.WRN_ExternCtorNoImplementation, "C").WithArguments("C.C()").WithLocation(7, 12));
    }

    [Fact]
    public void Extern_03()
    {
        var source = """
            partial class C
            {
                extern partial event System.Action E;
                partial event System.Action E { add { } remove { } }

                extern partial C();
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,40): error CS9401: Partial member 'C.E' must have a definition part.
            //     extern partial event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "E").WithArguments("C.E").WithLocation(3, 40),
            // (4,33): error CS9403: Partial member 'C.E' may not have multiple implementing declarations.
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "E").WithArguments("C.E").WithLocation(4, 33),
            // (4,33): error CS0102: The type 'C' already contains a definition for 'E'
            //     partial event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_DuplicateNameInClass, "E").WithArguments("C", "E").WithLocation(4, 33),
            // (6,20): error CS9401: Partial member 'C.C()' must have a definition part.
            //     extern partial C();
            Diagnostic(ErrorCode.ERR_PartialMemberMissingDefinition, "C").WithArguments("C.C()").WithLocation(6, 20),
            // (7,13): error CS9403: Partial member 'C.C()' may not have multiple implementing declarations.
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_PartialMemberDuplicateImplementation, "C").WithArguments("C.C()").WithLocation(7, 13),
            // (7,13): error CS0111: Type 'C' already defines a member called 'C' with the same parameter types
            //     partial C() { }
            Diagnostic(ErrorCode.ERR_MemberAlreadyExists, "C").WithArguments("C", "C").WithLocation(7, 13));
    }

    [Fact]
    public void EmitOrder_01()
    {
        verify("""
            partial class C
            {
                partial event System.Action E;
                partial event System.Action E { add { } remove { } }
                partial C();
                partial C() { }
            }
            """);

        verify("""
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial event System.Action E;
                partial C() { }
                partial C();
            }
            """);

        verify("""
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial C() { }
            }
            """, """
            partial class C
            {
                partial event System.Action E;
                partial C();
            }
            """);

        verify("""
            partial class C
            {
                partial C() { }
                partial event System.Action E { add { } remove { } }
            }
            """, """
            partial class C
            {
                partial C();
                partial event System.Action E;
            }
            """);

        void verify(params CSharpTestSource sources)
        {
            CompileAndVerify(sources,
                options: TestOptions.DebugDll.WithMetadataImportOptions(MetadataImportOptions.All),
                symbolValidator: validate)
                .VerifyDiagnostics();
        }

        static void validate(ModuleSymbol module)
        {
            var members = module.GlobalNamespace.GetTypeMember("C").GetMembers().Select(s => s.ToTestDisplayString()).Join("\n");
            AssertEx.AssertEqualToleratingWhitespaceDifferences("""
                void C.E.add
                void C.E.remove
                C..ctor()
                event System.Action C.E
                """, members);
        }
    }

    [Fact]
    public void EmitOrder_02()
    {
        verify("""
            partial class C
            {
                partial C();
                partial C() { }
                partial event System.Action E;
                partial event System.Action E { add { } remove { } }
            }
            """);

        verify("""
            partial class C
            {
                partial C() { }
                partial C();
                partial event System.Action E { add { } remove { } }
                partial event System.Action E;
            }
            """);

        verify("""
            partial class C
            {
                partial C();
                partial event System.Action E;
            }
            """, """
            partial class C
            {
                partial C() { }
                partial event System.Action E { add { } remove { } }
            }
            """);

        verify("""
            partial class C
            {
                partial event System.Action E;
                partial C();
            }
            """, """
            partial class C
            {
                partial event System.Action E { add { } remove { } }
                partial C() { }
            }
            """);

        void verify(params CSharpTestSource sources)
        {
            CompileAndVerify(sources,
                options: TestOptions.DebugDll.WithMetadataImportOptions(MetadataImportOptions.All),
                symbolValidator: validate)
                .VerifyDiagnostics();
        }

        static void validate(ModuleSymbol module)
        {
            var members = module.GlobalNamespace.GetTypeMember("C").GetMembers().Select(s => s.ToTestDisplayString()).Join("\n");
            AssertEx.AssertEqualToleratingWhitespaceDifferences("""
                C..ctor()
                void C.E.add
                void C.E.remove
                event System.Action C.E
                """, members);
        }
    }
}
