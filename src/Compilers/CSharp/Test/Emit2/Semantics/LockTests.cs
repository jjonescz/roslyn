// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Semantics;

public class LockTests : CSharpTestBase
{
    private const string LockTypeDefinition = """
        namespace System.Threading
        {
            public class Lock
            {
                public Scope EnterLockScope()
                {
                    Console.Write("E");
                    return new Scope();
                }

                public ref struct Scope
                {
                    public void Dispose()
                    {
                        Console.Write("D");
                    }
                }
            }
        }
        """;

    [Fact]
    public void LockVsUsing()
    {
        var source = """
            using System;
            using System.Threading;

            static class C
            {
                static readonly Lock _lock = new();

                static void Main()
                {
                    M1();
                    M2();
                }

                static void M1()
                {
                    Console.Write("1");
                    lock (_lock)
                    {
                        Console.Write("2");
                    }
                    Console.Write("3");
                }

                static void M2()
                {
                    Console.Write("1");
                    using (_lock.EnterLockScope())
                    {
                        Console.Write("2");
                    }
                    Console.Write("3");
                }
            }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, expectedOutput: "1E2D31E2D3",
            verify: Verification.FailsILVerify);
        verifier.VerifyDiagnostics();
        var il = """
            {
              // Code size       52 (0x34)
              .maxstack  1
              .locals init (System.Threading.Lock.Scope V_0)
              IL_0000:  ldstr      "1"
              IL_0005:  call       "void System.Console.Write(string)"
              IL_000a:  ldsfld     "System.Threading.Lock C._lock"
              IL_000f:  callvirt   "System.Threading.Lock.Scope System.Threading.Lock.EnterLockScope()"
              IL_0014:  stloc.0
              .try
              {
                IL_0015:  ldstr      "2"
                IL_001a:  call       "void System.Console.Write(string)"
                IL_001f:  leave.s    IL_0029
              }
              finally
              {
                IL_0021:  ldloca.s   V_0
                IL_0023:  call       "void System.Threading.Lock.Scope.Dispose()"
                IL_0028:  endfinally
              }
              IL_0029:  ldstr      "3"
              IL_002e:  call       "void System.Console.Write(string)"
              IL_0033:  ret
            }
            """;
        verifier.VerifyIL("C.M2", il);
        verifier.VerifyIL("C.M1", il);
    }

    [Fact]
    public void MissingEnterLockScope()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock { }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock.EnterLockScope'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock", "EnterLockScope").WithLocation(2, 1));
    }

    [Fact]
    public void EnterLockScopeReturnsVoid()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock
                {
                    public void EnterLockScope() { }
                }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock.EnterLockScope'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock", "EnterLockScope").WithLocation(2, 1));
    }

    [Fact]
    public void EnterLockScopeTakesArguments()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock
                {
                    public Scope EnterLockScope(int arg) => new Scope();

                    public ref struct Scope
                    {
                        public void Dispose() { }
                    }
                }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock.EnterLockScope'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock", "EnterLockScope").WithLocation(2, 1));
    }

    [Fact]
    public void MissingScopeDispose()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock
                {
                    public Scope EnterLockScope() => new Scope();

                    public struct Scope { }
                }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock+Scope.Dispose'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock+Scope", "Dispose").WithLocation(2, 1));
    }

    [Fact]
    public void ScopeDisposeReturnsNonVoid()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock
                {
                    public Scope EnterLockScope() => new Scope();

                    public ref struct Scope
                    {
                        public int Dispose() => 1;
                    }
                }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock+Scope.Dispose'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock+Scope", "Dispose").WithLocation(2, 1));
    }

    [Fact]
    public void ScopeDisposeTakesArguments()
    {
        var source = """
            System.Threading.Lock l = new();
            lock (l) { }

            namespace System.Threading
            {
                public class Lock
                {
                    public Scope EnterLockScope() => new Scope();

                    public ref struct Scope
                    {
                        public void Dispose(int x) { }
                    }
                }
            }
            """;
        CreateCompilation(source).VerifyEmitDiagnostics(
            // (2,1): error CS0656: Missing compiler required member 'System.Threading.Lock+Scope.Dispose'
            // lock (l) { }
            Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "lock (l) { }").WithArguments("System.Threading.Lock+Scope", "Dispose").WithLocation(2, 1));
    }

    [Fact]
    public void ExternalAssembly()
    {
        var lib = CreateCompilation(LockTypeDefinition).EmitToImageReference();
        var source = """
            using System;
            using System.Threading;
            
            Lock l = new Lock();
            lock (l) { Console.Write("L"); }
            """;
        var verifier = CompileAndVerify(source, [lib], expectedOutput: "ELD");
        verifier.VerifyDiagnostics();
    }

    [Fact]
    public void InPlace()
    {
        var source = """
            using System;
            using System.Threading;

            lock (new Lock())
            {
                Console.Write("L");
            }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, verify: Verification.FailsILVerify,
           expectedOutput: "ELD");
        verifier.VerifyDiagnostics();
    }

    [Theory, CombinatorialData]
    public void CastToObject([CombinatorialValues("object", "dynamic")] string type)
    {
        var source = $$"""
            using System;
            using System.Threading;

            Lock l = new();

            {{type}} o = l;
            lock (o) { Console.Write("1"); }
            
            lock (({{type}})l) { Console.Write("2"); }

            lock (l as {{type}}) { Console.Write("3"); }
            
            o = l as {{type}};
            lock (o) { Console.Write("4"); }

            static {{type}} Cast1<T>(T t) => t;
            lock (Cast1(l)) { Console.Write("5"); }

            static {{type}} Cast2<T>(T t) where T : class => t;
            lock (Cast2(l)) { Console.Write("6"); }

            static {{type}} Cast3<T>(T t) where T : Lock => t;
            lock (Cast3(l)) { Console.Write("7"); }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, verify: Verification.FailsILVerify,
           expectedOutput: "1234567");
        verifier.VerifyDiagnostics();
    }

    [Theory, CombinatorialData]
    public void CastToBase([CombinatorialValues("interface", "class")] string baseKind)
    {
        var source = $$"""
            using System;
            using System.Threading;

            ILock l1 = new Lock();
            lock (l1) { Console.Write("1"); }

            ILock l2 = new Lock();
            lock ((Lock)l2) { Console.Write("2"); }

            namespace System.Threading
            {
                public {{baseKind}} ILock { }

                public class Lock : ILock
                {
                    public Scope EnterLockScope()
                    {
                        Console.Write("E");
                        return new Scope();
                    }

                    public ref struct Scope
                    {
                        public void Dispose()
                        {
                            Console.Write("D");
                        }
                    }
                }
            }
            """;
        var verifier = CompileAndVerify(source, verify: Verification.FailsILVerify,
           expectedOutput: "1E2D");
        verifier.VerifyDiagnostics();
    }

    [Fact]
    public void DerivedLock()
    {
        var source = """
            using System;
            using System.Threading;

            DerivedLock l1 = new DerivedLock();
            lock (l1) { Console.Write("1"); }

            Lock l2 = l1;
            lock (l2) { Console.Write("2"); }

            DerivedLock l3 = (DerivedLock)l2;
            lock (l3) { Console.Write("3"); }

            namespace System.Threading
            {
                public class Lock
                {
                    public Scope EnterLockScope()
                    {
                        Console.Write("E");
                        return new Scope();
                    }

                    public ref struct Scope
                    {
                        public void Dispose()
                        {
                            Console.Write("D");
                        }
                    }
                }

                public class DerivedLock : Lock { }
            }
            """;
        var verifier = CompileAndVerify(source, verify: Verification.FailsILVerify,
           expectedOutput: "1E2D3");
        verifier.VerifyDiagnostics();
    }

    [Fact]
    public void Downcast()
    {
        var source = """
            using System;
            using System.Threading;

            object o = new Lock();
            lock ((Lock)o) { Console.Write("L"); }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, verify: Verification.FailsILVerify,
           expectedOutput: "ELD");
        verifier.VerifyDiagnostics();
    }

    [Theory]
    [InlineData("")]
    [InlineData("where T : class")]
    [InlineData("where T : Lock")]
    public void GenericParameter(string constraint)
    {
        var source = $$"""
            using System;
            using System.Threading;

            M(new Lock());

            static void M<T>(T t) {{constraint}}
            {
                lock (t) { Console.Write("L"); }
            }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, verify: Verification.FailsILVerify,
           expectedOutput: "L");
        verifier.VerifyDiagnostics();
    }

    [Fact]
    public void GenericParameter_Object()
    {
        var source = """
            using System;
            using System.Threading;

            M<object>(new Lock());

            static void M<T>(T t)
            {
                lock (t) { Console.Write("L"); }
            }
            """;
        var verifier = CompileAndVerify(new[] { source, LockTypeDefinition }, verify: Verification.FailsILVerify,
           expectedOutput: "L");
        verifier.VerifyDiagnostics();
    }
}
