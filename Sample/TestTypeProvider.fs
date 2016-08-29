namespace Sample

open System
open System.IO

open System.Reflection
open System.Reflection.Emit

open FSharp.Quotations
open FSharp.Core.CompilerServices

open Microsoft.CSharp

open SourceCodeTypeProvider

[<TypeProvider>]
type TestTypeProvider(config : TypeProviderConfig) as tp =
    inherit SourceCodeTypeProvider(config, new CSharpCodeProvider())
    do
        tp.Generate("""
            using System;
                namespace Sample {
                    public static class TestType {
                        public static readonly string Baz = "hello";
                        public static readonly string Bong = "world";
                    }
                }
        """)

[<assembly: TypeProviderAssembly>]
do()