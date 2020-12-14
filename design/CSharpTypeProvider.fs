namespace SourceCodeTypeProvider

open System
open System.IO
open System.Linq
open System.Reflection

// Use CodeDOM compiler (out of process) for .NET framework because
// the F# compiler loads an incompatible System.Collections.Immutable there,
// which breaks Roslyn in process.
#if NETFRAMEWORK
open System.CodeDom.Compiler
open Microsoft.CSharp
#else
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
#endif

open FSharp.Quotations
open FSharp.Core.CompilerServices

type CSharp = class end

[<TypeProvider>]
type CSharpTypeProvider(config : TypeProviderConfig) =
    let mutable assemblyNameCount = 1
    let mutable providedAssembly = None
    #if NETFRAMEWORK
    let compiler = new CSharpCodeProvider()
    #else
    let mutable compilation: CSharpCompilation option = None
    #endif

    let invalidate = Event<EventHandler,EventArgs>()

    let loadContext =
        new MetadataLoadContext(PathAssemblyResolver(config.ReferencedAssemblies))

    interface ITypeProvider with
        [<CLIEvent>]
        member __.Invalidate = invalidate.Publish
        member __.GetInvokerExpression(mb, parameters) = Expr.Call(unbox mb, Array.toList parameters)
        member __.Dispose() =
            loadContext.Dispose()
            #if NETFRAMEWORK
            compiler.Dispose()
            #endif

        member __.GetGeneratedAssemblyContents(assembly) =
            match providedAssembly with
            | Some bytes -> bytes
            | _ -> failwith "Static arguments were not applied"

        member __.GetStaticParameters(typeWithoutArguments) = [|
            { new ParameterInfo() with
                override __.Name = "SourceCode"
                override __.ParameterType = loadContext.CoreAssembly.GetType "System.String"
            }
        |]

        member __.GetNamespaces() = [|
            { new IProvidedNamespace with
                member __.GetNestedNamespaces() = [||]
                member __.NamespaceName = "SourceCodeTypeProvider"
                member __.GetTypes() = [| typeof<CSharp> |]
                member __.ResolveTypeName(name) = if name = "CSharp" then typeof<CSharp> else null
            } 
        |]

        member __.ApplyStaticArguments(typeWithoutArguments, typeNameWithArguments, staticArguments) =
            let sourceCode = staticArguments.[0] :?> string
            // Increment the assembly name each time so we can load a new one into the loadContext
            let assemblyName = sprintf "ProvidedTypes%d.dll" assemblyNameCount
            assemblyNameCount <- assemblyNameCount + 1
            #if NETFRAMEWORK
            let cp = CompilerParameters(
                        GenerateInMemory = false,
                        OutputAssembly = Path.Combine(config.TemporaryFolder, assemblyName),
                        TempFiles = new TempFileCollection(config.TemporaryFolder, false)
                        )
            let result = compiler.CompileAssemblyFromSource(cp, [| sourceCode |])
            if result.Errors.Count <> 0 then
                failwith (String.Join("; ", result.Errors.Cast<CompilerError>()))
            let bytes = File.ReadAllBytes(result.PathToAssembly)
            #else
            let sourceTree = CSharpSyntaxTree.ParseText(sourceCode)
            let references =
                config.ReferencedAssemblies
                |> Array.map (MetadataReference.CreateFromFile >> unbox)

            // Create/update the compilation
            compilation <-
                match compilation with
                | Some compilation -> compilation.AddSyntaxTrees(sourceTree).WithAssemblyName(assemblyName)
                | _ -> CSharpCompilation.Create(assemblyName, [| sourceTree |], references, CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                |> Some

            // Compile it into an assembly
            use ms = new MemoryStream()
            let result = compilation.Value.Emit(ms)
            if not result.Success then
                failwith (String.Join("; ", result.Diagnostics))
            let bytes = ms.ToArray()
            #endif
            providedAssembly <- Some bytes

            // Load the assembly and try to find our specific type
            let name = Array.last typeNameWithArguments
            match loadContext.LoadFromByteArray(bytes).GetTypes() |> Array.tryFind (fun ty -> ty.Name = name) with
            | Some ty -> ty
            | _ -> failwithf "Could not find type with expected name: '%s'" name
