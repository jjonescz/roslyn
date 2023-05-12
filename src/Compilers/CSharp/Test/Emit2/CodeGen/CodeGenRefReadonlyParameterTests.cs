// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.CodeGen
{
    public class CodeGenRefReadonlyParameterTests : CSharpTestBase
    {
        [Fact]
        public void EmitMethod()
        {
            var source = """
                class C
                {
                    void M(ref readonly int p) { }
                }
                """;
            var verifier = CompileAndVerify(source);
            verifier.VerifyDiagnostics();
            verifier.VerifyTypeIL("C", """
                .class private auto ansi beforefieldinit C
                	extends [netstandard]System.Object
                {
                	// Methods
                	.method private hidebysig 
                		instance void M (
                			[in] int32& p
                		) cil managed 
                	{
                		// Method begins at RVA 0x2067
                		// Code size 1 (0x1)
                		.maxstack 8
                		IL_0000: ret
                	} // end of method C::M
                	.method public hidebysig specialname rtspecialname 
                		instance void .ctor () cil managed 
                	{
                		// Method begins at RVA 0x2069
                		// Code size 7 (0x7)
                		.maxstack 8
                		IL_0000: ldarg.0
                		IL_0001: call instance void [netstandard]System.Object::.ctor()
                		IL_0006: ret
                	} // end of method C::.ctor
                } // end of class C
                """);
        }
    }
}
