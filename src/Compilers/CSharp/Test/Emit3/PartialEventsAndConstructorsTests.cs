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
    public void Event_PartialLast([CombinatorialValues("", "public")] string modifier)
    {
        var source = $$"""
            partial class C
            {
                {{modifier}}
                partial event System.Action E;
                {{modifier}}
                partial event System.Action E { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics();
    }

    [Fact]
    public void Event_PartialNotLast()
    {
        var source = """
            partial class C
            {
                partial public event System.Action E;
                partial public event System.Action E { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public event System.Action E;
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(3, 5),
            // (4,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public event System.Action E { add { } remove { } }
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(4, 5));
    }

    [Fact]
    public void Event_Initializer_Single()
    {
        var source = """
            partial class C
            {
                partial event System.Action E = null;
                partial event System.Action E { add { } remove { } }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,33): error CS9408: 'C.E': partial event cannot have initializer
            //     partial event System.Action E = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,33): warning CS0414: The field 'C.E' is assigned but its value is never used
            //     partial event System.Action E = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "E").WithArguments("C.E").WithLocation(3, 33));
    }

    [Fact]
    public void Event_Initializer_Multiple_01()
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
            // (3,36): error CS9408: 'C.F': partial event cannot have initializer
            //     partial event System.Action E, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "F").WithArguments("C.F").WithLocation(3, 36),
            // (3,36): warning CS0414: The field 'C.F' is assigned but its value is never used
            //     partial event System.Action E, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "F").WithArguments("C.F").WithLocation(3, 36));
    }

    [Fact]
    public void Event_Initializer_Multiple_02()
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
            // (3,33): error CS9408: 'C.E': partial event cannot have initializer
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,33): warning CS0414: The field 'C.E' is assigned but its value is never used
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "E").WithArguments("C.E").WithLocation(3, 33),
            // (3,43): error CS9408: 'C.F': partial event cannot have initializer
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.ERR_PartialEventInitializer, "F").WithArguments("C.F").WithLocation(3, 43),
            // (3,43): warning CS0414: The field 'C.F' is assigned but its value is never used
            //     partial event System.Action E = null, F = null;
            Diagnostic(ErrorCode.WRN_UnreferencedFieldAssg, "F").WithArguments("C.F").WithLocation(3, 43));
    }

    [Theory, CombinatorialData]
    public void Constructor_PartialLast([CombinatorialValues("", "public")] string modifier)
    {
        var source = $$"""
            partial class C
            {
                {{modifier}}
                partial C();
                {{modifier}}
                partial C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics();
    }

    [Fact]
    public void Constructor_PartialNotLast()
    {
        var source = """
            partial class C
            {
                partial public C();
                partial public C() { }
            }
            """;
        CreateCompilation(source).VerifyDiagnostics(
            // (3,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public C();
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(3, 5),
            // (4,5): error CS0267: The 'partial' modifier can only appear immediately before 'class', 'record', 'struct', 'interface', 'event', an instance constructor identifier, or a method or property return type.
            //     partial public C() { }
            Diagnostic(ErrorCode.ERR_PartialMisplaced, "partial").WithLocation(4, 5));
    }

    [Fact]
    public void Constructor_StaticPartial()
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
}
