﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.CodeFixes.FullyQualify;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.FullyQualify
{
    [Trait(Traits.Feature, Traits.Features.CodeActionsFullyQualify)]
    public class FullyQualifyTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        public FullyQualifyTests(ITestOutputHelper logger)
          : base(logger)
        {
        }

        internal override (DiagnosticAnalyzer, CodeFixProvider) CreateDiagnosticProviderAndFixer(Workspace workspace)
            => (null, new CSharpFullyQualifyCodeFixProvider());

        protected override ImmutableArray<CodeAction> MassageActions(ImmutableArray<CodeAction> actions)
            => FlattenActions(actions);

        [Fact]
        public async Task TestTypeFromMultipleNamespaces1()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    [|IDictionary|] Method()
    {
        Goo();
    }
}",
@"class Class
{
    System.Collections.IDictionary Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestTypeFromMultipleNamespaces2()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    [|IDictionary|] Method()
    {
        Goo();
    }
}",
@"class Class
{
    System.Collections.Generic.IDictionary Method()
    {
        Goo();
    }
}",
index: 1);
        }

        [Fact]
        public async Task TestGenericWithNoArgs()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    [|List|] Method()
    {
        Goo();
    }
}",
@"class Class
{
    System.Collections.Generic.List Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestGenericWithCorrectArgs()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    [|List<int>|] Method()
    {
        Goo();
    }
}",
@"class Class
{
    System.Collections.Generic.List<int> Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestSmartTagDisplayText()
        {
            await TestSmartTagTextAsync(
@"class Class
{
    [|List<int>|] Method()
    {
        Goo();
    }
}",
"System.Collections.Generic.List");
        }

        [Fact]
        public async Task TestGenericWithWrongArgs()
        {
            await TestMissingInRegularAndScriptAsync(
@"class Class
{
    [|List<int, string>|] Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestNotOnVar1()
        {
            await TestMissingInRegularAndScriptAsync(
@"namespace N
{
    class var { }
}

class C
{
    void M()
    {
        [|var|]
    }
}
");
        }

        [Fact]
        public async Task TestNotOnVar2()
        {
            await TestMissingInRegularAndScriptAsync(
@"namespace N
{
    class Bar { }
}

class C
{
    void M()
    {
        [|var|]
    }
}
");
        }

        [Fact]
        public async Task TestGenericInLocalDeclaration()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Goo()
    {
        [|List<int>|] a = new List<int>();
    }
}",
@"class Class
{
    void Goo()
    {
        System.Collections.Generic.List<int> a = new List<int>();
    }
}");
        }

        [Fact]
        public async Task TestGenericItemType()
        {
            await TestInRegularAndScriptAsync(
@"using System.Collections.Generic;

class Class
{
    List<[|Int32|]> l;
}",
@"using System.Collections.Generic;

class Class
{
    List<System.Int32> l;
}");
        }

        [Fact]
        public async Task TestGenerateWithExistingUsings()
        {
            await TestInRegularAndScriptAsync(
@"using System;

class Class
{
    [|List<int>|] Method()
    {
        Goo();
    }
}",
@"using System;

class Class
{
    System.Collections.Generic.List<int> Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestGenerateInNamespace()
        {
            await TestInRegularAndScriptAsync(
@"namespace N
{
    class Class
    {
        [|List<int>|] Method()
        {
            Goo();
        }
    }
}",
@"namespace N
{
    class Class
    {
        System.Collections.Generic.List<int> Method()
        {
            Goo();
        }
    }
}");
        }

        [Fact]
        public async Task TestGenerateInNamespaceWithUsings()
        {
            await TestInRegularAndScriptAsync(
@"namespace N
{
    using System;

    class Class
    {
        [|List<int>|] Method()
        {
            Goo();
        }
    }
}",
@"namespace N
{
    using System;

    class Class
    {
        System.Collections.Generic.List<int> Method()
        {
            Goo();
        }
    }
}");
        }

        [Fact]
        public async Task TestExistingUsing()
        {
            await TestActionCountAsync(
@"using System.Collections.Generic;

class Class
{
    [|IDictionary|] Method()
    {
        Goo();
    }
}",
count: 1);

            await TestInRegularAndScriptAsync(
@"using System.Collections.Generic;

class Class
{
    [|IDictionary|] Method()
    {
        Goo();
    }
}",
@"using System.Collections.Generic;

class Class
{
    System.Collections.IDictionary Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestMissingIfUniquelyBound()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class Class
{
    [|String|] Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestMissingIfUniquelyBoundGeneric()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System.Collections.Generic;

class Class
{
    [|List<int>|] Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TestOnEnum()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Goo()
    {
        var a = [|Colors|].Red;
    }
}

namespace A
{
    enum Colors
    {
        Red,
        Green,
        Blue
    }
}",
@"class Class
{
    void Goo()
    {
        var a = A.Colors.Red;
    }
}

namespace A
{
    enum Colors
    {
        Red,
        Green,
        Blue
    }
}");
        }

        [Fact]
        public async Task TestOnClassInheritance()
        {
            await TestInRegularAndScriptAsync(
@"class Class : [|Class2|]
{
}

namespace A
{
    class Class2
    {
    }
}",
@"class Class : A.Class2
{
}

namespace A
{
    class Class2
    {
    }
}");
        }

        [Fact]
        public async Task TestOnImplementedInterface()
        {
            await TestInRegularAndScriptAsync(
@"class Class : [|IGoo|]
{
}

namespace A
{
    interface IGoo
    {
    }
}",
@"class Class : A.IGoo
{
}

namespace A
{
    interface IGoo
    {
    }
}");
        }

        [Fact]
        public async Task TestAllInBaseList()
        {
            await TestInRegularAndScriptAsync(
@"class Class : [|IGoo|], Class2
{
}

namespace A
{
    class Class2
    {
    }
}

namespace B
{
    interface IGoo
    {
    }
}",
@"class Class : B.IGoo, Class2
{
}

namespace A
{
    class Class2
    {
    }
}

namespace B
{
    interface IGoo
    {
    }
}");

            await TestInRegularAndScriptAsync(
@"class Class : B.IGoo, [|Class2|]
{
}

namespace A
{
    class Class2
    {
    }
}

namespace B
{
    interface IGoo
    {
    }
}",
@"class Class : B.IGoo, A.Class2
{
}

namespace A
{
    class Class2
    {
    }
}

namespace B
{
    interface IGoo
    {
    }
}");
        }

        [Fact]
        public async Task TestAttributeUnexpanded()
        {
            await TestInRegularAndScriptAsync(
@"[[|Obsolete|]]
class Class
{
}",
@"[System.Obsolete]
class Class
{
}");
        }

        [Fact]
        public async Task TestAttributeExpanded()
        {
            await TestInRegularAndScriptAsync(
@"[[|ObsoleteAttribute|]]
class Class
{
}",
@"[System.ObsoleteAttribute]
class Class
{
}");
        }

        [Fact, WorkItem(527360, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/527360")]
        public async Task TestExtensionMethods()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System.Collections.Generic;

class Goo
{
    void Bar()
    {
        var values = new List<int>() { 1, 2, 3 };
        values.[|Where|](i => i > 1);
    }
}");
        }

        [Fact, WorkItem(538018, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538018")]
        public async Task TestAfterNew()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Goo()
    {
        List<int> l;
        l = new [|List<int>|]();
    }
}",
@"class Class
{
    void Goo()
    {
        List<int> l;
        l = new System.Collections.Generic.List<int>();
    }
}");
        }

        [Fact]
        public async Task TestArgumentsInMethodCall()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Test()
    {
        Console.WriteLine([|DateTime|].Today);
    }
}",
@"class Class
{
    void Test()
    {
        Console.WriteLine(System.DateTime.Today);
    }
}");
        }

        [Fact]
        public async Task TestCallSiteArgs()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Test([|DateTime|] dt)
    {
    }
}",
@"class Class
{
    void Test(System.DateTime dt)
    {
    }
}");
        }

        [Fact]
        public async Task TestUsePartialClass()
        {
            await TestInRegularAndScriptAsync(
@"namespace A
{
    public class Class
    {
        [|PClass|] c;
    }
}

namespace B
{
    public partial class PClass
    {
    }
}",
@"namespace A
{
    public class Class
    {
        B.PClass c;
    }
}

namespace B
{
    public partial class PClass
    {
    }
}");
        }

        [Fact]
        public async Task TestGenericClassInNestedNamespace()
        {
            await TestInRegularAndScriptAsync(
@"namespace A
{
    namespace B
    {
        class GenericClass<T>
        {
        }
    }
}

namespace C
{
    class Class
    {
        [|GenericClass<int>|] c;
    }
}",
@"namespace A
{
    namespace B
    {
        class GenericClass<T>
        {
        }
    }
}

namespace C
{
    class Class
    {
        A.B.GenericClass<int> c;
    }
}");
        }

        [Fact]
        public async Task TestBeforeStaticMethod()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    void Test()
    {
        [|Math|].Sqrt();
    }",
@"class Class
{
    void Test()
    {
        System.Math.Sqrt();
    }");
        }

        [Fact, WorkItem(538136, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538136")]
        public async Task TestBeforeNamespace()
        {
            await TestInRegularAndScriptAsync(
@"namespace A
{
    class Class
    {
        [|C|].Test t;
    }
}

namespace B
{
    namespace C
    {
        class Test
        {
        }
    }
}",
@"namespace A
{
    class Class
    {
        B.C.Test t;
    }
}

namespace B
{
    namespace C
    {
        class Test
        {
        }
    }
}");
        }

        [Fact, WorkItem(527395, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/527395")]
        public async Task TestSimpleNameWithLeadingTrivia()
        {
            await TestInRegularAndScriptAsync(
@"class Class { void Test() { /*goo*/[|Int32|] i; } }",
@"class Class { void Test() { /*goo*/System.Int32 i; } }");
        }

        [Fact, WorkItem(527395, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/527395")]
        public async Task TestGenericNameWithLeadingTrivia()
        {
            await TestInRegularAndScriptAsync(
@"class Class { void Test() { /*goo*/[|List<int>|] l; } }",
@"class Class { void Test() { /*goo*/System.Collections.Generic.List<int> l; } }");
        }

        [Fact, WorkItem(538740, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538740")]
        public async Task TestFullyQualifyTypeName()
        {
            await TestInRegularAndScriptAsync(
@"public class Program
{
    public class Inner
    {
    }
}

class Test
{
    [|Inner|] i;
}",
@"public class Program
{
    public class Inner
    {
    }
}

class Test
{
    Program.Inner i;
}");
        }

        [Fact, WorkItem(26887, "https://github.com/dotnet/roslyn/issues/26887")]
        public async Task TestFullyQualifyUnboundIdentifier3()
        {
            await TestInRegularAndScriptAsync(
@"public class Program
{
    public class Inner
    {
    }
}

class Test
{
    public [|Inner|] Name
}",
@"public class Program
{
    public class Inner
    {
    }
}

class Test
{
    public Program.Inner Name
}");
        }

        [Fact, WorkItem(538740, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538740")]
        public async Task TestFullyQualifyTypeName_NotForGenericType()
        {
            await TestMissingInRegularAndScriptAsync(
@"class Program<T>
{
    public class Inner
    {
    }
}

class Test
{
    [|Inner|] i;
}");
        }

        [Fact, WorkItem(538764, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538764")]
        public async Task TestFullyQualifyThroughAlias()
        {
            await TestInRegularAndScriptAsync(
@"using Alias = System;

class C
{
    [|Int32|] i;
}",
@"using Alias = System;

class C
{
    Alias.Int32 i;
}");
        }

        [Fact, WorkItem(538763, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538763")]
        public async Task TestFullyQualifyPrioritizeTypesOverNamespaces1()
        {
            await TestInRegularAndScriptAsync(
@"namespace Outer
{
    namespace C
    {
        class C
        {
        }
    }
}

class Test
{
    [|C|] c;
}",
@"namespace Outer
{
    namespace C
    {
        class C
        {
        }
    }
}

class Test
{
    Outer.C.C c;
}");
        }

        [Fact, WorkItem(538763, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/538763")]
        public async Task TestFullyQualifyPrioritizeTypesOverNamespaces2()
        {
            await TestInRegularAndScriptAsync(
@"namespace Outer
{
    namespace C
    {
        class C
        {
        }
    }
}

class Test
{
    [|C|] c;
}",
@"namespace Outer
{
    namespace C
    {
        class C
        {
        }
    }
}

class Test
{
    Outer.C c;
}",
index: 1);
        }

        [Fact, WorkItem(539853, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/539853")]
        public async Task BugFix5950()
        {
            await TestAsync(
@"using System.Console; WriteLine([|Expression|].Constant(123));",
@"using System.Console; WriteLine(System.Linq.Expressions.Expression.Constant(123));",
parseOptions: GetScriptOptions());
        }

        [Fact, WorkItem(540318, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/540318")]
        public async Task TestAfterAlias()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        System::[|Console|] :: WriteLine(""TEST"");
    }
}");
        }

        [Fact, WorkItem(540942, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/540942")]
        public async Task TestMissingOnIncompleteStatement()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;
using System.IO;

class C
{
    static void Main(string[] args)
    {
        [|Path|] }
}");
        }

        [Fact, WorkItem(542643, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/542643")]
        public async Task TestAssemblyAttribute()
        {
            await TestInRegularAndScriptAsync(
@"[assembly: [|InternalsVisibleTo|](""Project"")]",
@"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(""Project"")]");
        }

        [Fact, WorkItem(543388, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/543388")]
        public async Task TestMissingOnAliasName()
        {
            await TestMissingInRegularAndScriptAsync(
@"using [|GIBBERISH|] = Goo.GIBBERISH;

class Program
{
    static void Main(string[] args)
    {
        GIBBERISH x;
    }
}

namespace Goo
{
    public class GIBBERISH
    {
    }
}");
        }

        [Fact]
        public async Task TestMissingOnAttributeOverloadResolutionError()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System.Runtime.InteropServices;

class M
{
    [[|DllImport|]()]
    static extern int? My();
}");
        }

        [Fact, WorkItem(544950, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/544950")]
        public async Task TestNotOnAbstractConstructor()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System.IO;

class Program
{
    static void Main(string[] args)
    {
        var s = new [|Stream|]();
    }
}");
        }

        [Fact, WorkItem(545774, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/545774")]
        public async Task TestAttribute()
        {
            var input = @"[ assembly : [|Guid|] ( ""9ed54f84-a89d-4fcd-a854-44251e925f09"" ) ] ";
            await TestActionCountAsync(input, 2);

            await TestInRegularAndScriptAsync(
input,
@"[ assembly : System.Runtime.InteropServices.Guid( ""9ed54f84-a89d-4fcd-a854-44251e925f09"" ) ] ");
        }

        [Fact, WorkItem(546027, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/546027")]
        public async Task TestGeneratePropertyFromAttribute()
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

[AttributeUsage(AttributeTargets.Class)]
class MyAttrAttribute : Attribute
{
}

[MyAttr(123, [|Version|] = 1)]
class D
{
}");
        }

        [Fact, WorkItem(775448, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/775448")]
        public async Task ShouldTriggerOnCS0308()
        {
            // CS0308: The non-generic type 'A' cannot be used with type arguments
            await TestInRegularAndScriptAsync(
@"using System.Collections;

class Test
{
    static void Main(string[] args)
    {
        [|IEnumerable<int>|] f;
    }
}",
@"using System.Collections;

class Test
{
    static void Main(string[] args)
    {
        System.Collections.Generic.IEnumerable<int> f;
    }
}");
        }

        [Fact, WorkItem(947579, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/947579")]
        public async Task AmbiguousTypeFix()
        {
            await TestInRegularAndScriptAsync(
@"using n1;
using n2;

class B
{
    void M1()
    {
        [|var a = new A();|]
    }
}

namespace n1
{
    class A
    {
    }
}

namespace n2
{
    class A
    {
    }
}",
@"using n1;
using n2;

class B
{
    void M1()
    {
        var a = new n1.A();
    }
}

namespace n1
{
    class A
    {
    }
}

namespace n2
{
    class A
    {
    }
}");
        }

        [Fact, WorkItem(995857, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/995857")]
        public async Task NonPublicNamespaces()
        {
            await TestInRegularAndScriptAsync(
@"namespace MS.Internal.Xaml
{
    private class A
    {
    }
}

namespace System.Xaml
{
    public class A
    {
    }
}

public class Program
{
    static void M()
    {
        [|Xaml|]
    }
}",
@"namespace MS.Internal.Xaml
{
    private class A
    {
    }
}

namespace System.Xaml
{
    public class A
    {
    }
}

public class Program
{
    static void M()
    {
        System.Xaml
    }
}");

            await TestInRegularAndScriptAsync(
@"namespace MS.Internal.Xaml
{
    public class A
    {
    }
}

namespace System.Xaml
{
    public class A
    {
    }
}

public class Program
{
    static void M()
    {
        [|Xaml|]
    }
}",
@"namespace MS.Internal.Xaml
{
    public class A
    {
    }
}

namespace System.Xaml
{
    public class A
    {
    }
}

public class Program
{
    static void M()
    {
        MS.Internal.Xaml
    }
}", index: 1);
        }

        [Fact, WorkItem(11071, "https://github.com/dotnet/roslyn/issues/11071")]
        public async Task AmbiguousFixOrdering()
        {
            await TestInRegularAndScriptAsync(
@"using n1;
using n2;

[[|Inner|].C]
class B
{
}

namespace n1
{
    namespace Inner
    {
    }
}

namespace n2
{
    namespace Inner
    {
        class CAttribute
        {
        }
    }
}",
@"using n1;
using n2;

[n2.Inner.C]
class B
{
}

namespace n1
{
    namespace Inner
    {
    }
}

namespace n2
{
    namespace Inner
    {
        class CAttribute
        {
        }
    }
}");
        }

        [Fact]
        public async Task TupleTest()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    ([|IDictionary|], string) Method()
    {
        Goo();
    }
}",
@"class Class
{
    (System.Collections.IDictionary, string) Method()
    {
        Goo();
    }
}");
        }

        [Fact]
        public async Task TupleWithOneName()
        {
            await TestInRegularAndScriptAsync(
@"class Class
{
    ([|IDictionary|] a, string) Method()
    {
        Goo();
    }
}",
@"class Class
{
    (System.Collections.IDictionary a, string) Method()
    {
        Goo();
    }
}");
        }

        [Fact, WorkItem(18275, "https://github.com/dotnet/roslyn/issues/18275")]
        public async Task TestContextualKeyword1()
        {
            await TestMissingInRegularAndScriptAsync(
@"
namespace N
{
    class nameof
    {
    }
}

class C
{
    void M()
    {
        [|nameof|]
    }
}");
        }

        [Fact, WorkItem(18623, "https://github.com/dotnet/roslyn/issues/18623")]
        public async Task TestDoNotQualifyToTheSameTypeToFixWrongArity()
        {
            await TestMissingInRegularAndScriptAsync(
@"
using System.Collections.Generic;

class Program : [|IReadOnlyCollection|]
{
}");
        }

        [Fact, WorkItem(19575, "https://github.com/dotnet/roslyn/issues/19575")]
        public async Task TestNoNonGenericsWithGenericCodeParsedAsExpression()
        {
            var code = @"
class C
{
    private void GetEvaluationRuleNames()
    {
        [|IEnumerable|] < Int32 >
        return ImmutableArray.CreateRange();
    }
}";
            await TestActionCountAsync(code, count: 1);

            await TestInRegularAndScriptAsync(
code,
@"
class C
{
    private void GetEvaluationRuleNames()
    {
        System.Collections.Generic.IEnumerable < Int32 >
        return ImmutableArray.CreateRange();
    }
}");
        }

        [Fact, WorkItem(49986, "https://github.com/dotnet/roslyn/issues/49986")]
        public async Task TestInUsingContext_Type()
        {
            await TestInRegularAndScriptAsync(
@"using [|Math|];

class Class
{
    void Test()
    {
        Sqrt(1);
    }",
@"using static System.Math;

class Class
{
    void Test()
    {
        Sqrt(1);
    }");
        }

        [Fact, WorkItem(49986, "https://github.com/dotnet/roslyn/issues/49986")]
        public async Task TestInUsingContext_Namespace()
        {
            await TestInRegularAndScriptAsync(
@"using [|Collections|];

class Class
{
    void Test()
    {
        Sqrt(1);
    }",
@"using System.Collections;

class Class
{
    void Test()
    {
        Sqrt(1);
    }");
        }

        [Fact, WorkItem(49986, "https://github.com/dotnet/roslyn/issues/49986")]
        public async Task TestInUsingContext_UsingStatic()
        {
            await TestInRegularAndScriptAsync(
@"using static [|Math|];

class Class
{
    void Test()
    {
        Sqrt(1);
    }",
@"using static System.Math;

class Class
{
    void Test()
    {
        Sqrt(1);
    }");
        }

        [Fact, WorkItem(51274, "https://github.com/dotnet/roslyn/issues/51274")]
        public async Task TestInUsingContext_UsingAlias()
        {
            await TestInRegularAndScriptAsync(
@"using M = [|Math|]",
@"using M = System.Math");
        }

        [Fact, WorkItem(54544, "https://github.com/dotnet/roslyn/issues/54544")]
        public async Task TestAddUsingsEditorBrowsableNeverSameProject()
        {
            const string InitialWorkspace = @"
<Workspace>
    <Project Language=""C#"" AssemblyName=""lib"" CommonReferences=""true"">
        <Document FilePath=""lib.cs"">
using System.ComponentModel;
namespace ProjectLib
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class Project
    {
    }
}
        </Document>
        <Document FilePath=""Program.cs"">
class Program
{
    static void Main(string[] args)
    {
        Project p = new [|Project()|];
    }
}
</Document>
    </Project>
</Workspace>";

            const string ExpectedDocumentText = @"
class Program
{
    static void Main(string[] args)
    {
        Project p = new [|ProjectLib.Project()|];
    }
}
";

            await TestInRegularAndScript1Async(InitialWorkspace, ExpectedDocumentText);
        }

        [Fact, WorkItem(54544, "https://github.com/dotnet/roslyn/issues/54544")]
        public async Task TestAddUsingsEditorBrowsableNeverDifferentProject()
        {
            const string InitialWorkspace = @"
<Workspace>
    <Project Language=""Visual Basic"" AssemblyName=""lib"" CommonReferences=""true"">
        <Document FilePath=""lib.vb"">
imports System.ComponentModel
namespace ProjectLib
    &lt;EditorBrowsable(EditorBrowsableState.Never)&gt;
    public class Project
    end class
end namespace
        </Document>
    </Project>
    <Project Language=""C#"" AssemblyName=""Console"" CommonReferences=""true"">
        <ProjectReference>lib</ProjectReference>
        <Document FilePath=""Program.cs"">
class Program
{
    static void Main(string[] args)
    {
        [|Project|] p = new Project();
    }
}
</Document>
    </Project>
</Workspace>";
            await TestMissingAsync(InitialWorkspace);
        }

        [Fact, WorkItem(54544, "https://github.com/dotnet/roslyn/issues/54544")]
        public async Task TestAddUsingsEditorBrowsableAdvancedDifferentProjectOptionOn()
        {
            const string InitialWorkspace = @"
<Workspace>
    <Project Language=""Visual Basic"" AssemblyName=""lib"" CommonReferences=""true"">
        <Document FilePath=""lib.vb"">
imports System.ComponentModel
namespace ProjectLib
    &lt;EditorBrowsable(EditorBrowsableState.Advanced)&gt;
    public class Project
    end class
end namespace
        </Document>
    </Project>
    <Project Language=""C#"" AssemblyName=""Console"" CommonReferences=""true"">
        <ProjectReference>lib</ProjectReference>
        <Document FilePath=""Program.cs"">
class Program
{
    static void Main(string[] args)
    {
        [|Project|] p = new Project();
    }
}
</Document>
    </Project>
</Workspace>";

            const string ExpectedDocumentText = @"
class Program
{
    static void Main(string[] args)
    {
        ProjectLib.Project p = new Project();
    }
}
";
            await TestInRegularAndScript1Async(InitialWorkspace, ExpectedDocumentText);
        }

        [Fact, WorkItem(54544, "https://github.com/dotnet/roslyn/issues/54544")]
        public async Task TestAddUsingsEditorBrowsableAdvancedDifferentProjectOptionOff()
        {
            var initialWorkspace = @"
<Workspace>
    <Project Language=""Visual Basic"" AssemblyName=""lib"" CommonReferences=""true"">
        <Document FilePath=""lib.vb"">
imports System.ComponentModel
namespace ProjectLib
    &lt;EditorBrowsable(EditorBrowsableState.Advanced)&gt;
    public class Project
    end class
end namespace
        </Document>
    </Project>
    <Project Language=""C#"" AssemblyName=""Console"" CommonReferences=""true"">
        <ProjectReference>lib</ProjectReference>
        <Document FilePath=""Program.cs"">
class Program
{
    static void Main(string[] args)
    {
        [|Project|] p = new Project();
    }
}
</Document>
    </Project>
</Workspace>";

            await TestMissingAsync(initialWorkspace, new TestParameters(
                globalOptions: Option(CompletionOptionsStorage.HideAdvancedMembers, true)));
        }
    }
}
