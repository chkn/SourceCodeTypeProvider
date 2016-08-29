namespace SourceCodeTypeProvider

open System
open System.IO
open System.Reflection
open System.CodeDom.Compiler
open System.Collections.Generic

open FSharp.Quotations
open FSharp.Core.CompilerServices

[<AbstractClass>]
type SourceCodeTypeProvider(config : TypeProviderConfig, compiler : CodeDomProvider) =
    let mutable providedAssembly = None
    let invalidate = Event<EventHandler,EventArgs>()
    let cp = CompilerParameters(
               GenerateInMemory = false,
               OutputAssembly = Path.Combine(config.TemporaryFolder, "ProvidedTypes.dll"),
               TempFiles = new TempFileCollection(config.TemporaryFolder, false)
             )

    member __.Generate(sourceCode) =
        if providedAssembly.IsSome then invalidOp "Already generated"
        let result = compiler.CompileAssemblyFromSource(cp, [| sourceCode |])
        let asm = Assembly.LoadFrom(result.PathToAssembly)
        let types = asm.GetTypes()
        let namespaces =
            let dict = Dictionary<_,List<_>>()
            for t in types do
                match dict.TryGetValue(t.Namespace) with
                | true, ns -> ns.Add(t)
                | _, _ ->
                    let ns = List<_>()
                    ns.Add(t)
                    dict.Add(t.Namespace, ns)
            dict
            |> Seq.map (fun kv ->
                { new IProvidedNamespace with
                    member x.NamespaceName = kv.Key
                    member x.GetNestedNamespaces() = [||] //FIXME
                    member x.GetTypes() = kv.Value.ToArray()
                    member x.ResolveTypeName(typeName: string) = null
                }
            )
            |> Seq.toArray
        providedAssembly <- Some(File.ReadAllBytes(result.PathToAssembly), namespaces)    

    interface ITypeProvider with
        [<CLIEvent>]
        member x.Invalidate = invalidate.Publish
        member x.GetStaticParameters(typeWithoutArguments) = [||]
        member x.GetGeneratedAssemblyContents(assembly) =
            match providedAssembly with
            | Some(bytes, _) -> bytes
            | _ -> failwith "Generate was never called"
        member x.GetNamespaces() =
            match providedAssembly with
            | Some(_, namespaces) -> namespaces
            | _ -> failwith "Generate was never called"
        member x.ApplyStaticArguments(typeWithoutArguments, typeNameWithArguments, staticArguments) = null
        member x.GetInvokerExpression(mb, parameters) = Expr.Call(mb :?> MethodInfo, Array.toList parameters)
        member x.Dispose() = compiler.Dispose()
