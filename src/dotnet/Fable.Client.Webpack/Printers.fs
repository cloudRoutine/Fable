module Fable.Client.Webpack.Printers

open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open Newtonsoft.Json
open Fable
open Fable.AST

type CompilerMessage =
    | Error of message: string * stack: string option
    | Log of message: string
    static member toDic = function
        | Error (msg, Some stack) ->
            dict [ ("type", "ERROR"); ("message", msg); ("stack", stack) ]
        | Error (msg, None) ->
            dict [ ("type", "ERROR"); ("message", msg) ]
        | Log msg ->
            dict [ ("type", "LOG"); ("message", msg) ]

let printMessages (msgs: #seq<CompilerMessage>) =
    msgs
    |> Seq.map (CompilerMessage.toDic >> JsonConvert.SerializeObject)
    |> Seq.iter Console.Out.WriteLine

let printFile (com: ICompiler) =
    let jsonSettings =
        JsonSerializerSettings(
            Converters=[|Json.ErasedUnionConverter()|],
            NullValueHandling=NullValueHandling.Ignore,
            StringEscapeHandling=StringEscapeHandling.EscapeNonAscii)
    fun (file: AST.Babel.Program) ->
        let json = JsonConvert.SerializeObject (file, jsonSettings)
        json |> Console.Out.WriteLine
        if com.Options.extra |> Map.containsKey "saveBabelAst" then
            let filePath = Path.ChangeExtension(file.fileName, ".babel.ast")
            File.WriteAllText(filePath, json)

let printException (ex: Exception) =
    let rec innerStack (ex: Exception) =
        if isNull ex.InnerException then ex.StackTrace else innerStack ex.InnerException
    let msg, stackTrace =
        match ex with
        // Don't print stack trace for known Fable errors
        | :? FableError as err -> err.FormattedMessage, None
        | ex -> ex.Message, Some(innerStack ex)
    printMessages [Error(msg, stackTrace)]

let attribsOfSymbol (s:FSharpSymbol) =
    let tryOr f def =
        try f() with _ -> def
    [ match s with
        | :? FSharpField as v ->
            yield "field"
            if v.IsCompilerGenerated then yield "compgen"
            if v.IsDefaultValue then yield "default"
            if v.IsMutable then yield "mutable"
            if v.IsVolatile then yield "volatile"
            if v.IsStatic then yield "static"
            if v.IsLiteral then yield sprintf "%A" v.LiteralValue.Value

        | :? FSharpEntity as v ->
            v.TryFullName |> ignore // check there is no failure here
            match v.BaseType with
            | Some t when t.HasTypeDefinition && t.TypeDefinition.TryFullName.IsSome ->
                yield sprintf "inherits %s" t.TypeDefinition.FullName
            | _ -> ()
            if v.IsNamespace then yield "namespace"
            if v.IsFSharpModule then yield "module"
            if v.IsByRef then yield "byref"
            if v.IsClass then yield "class"
            if v.IsDelegate then yield "delegate"
            if v.IsEnum then yield "enum"
            if v.IsFSharpAbbreviation then yield "abbrev"
            if v.IsFSharpExceptionDeclaration then yield "exception"
            if v.IsFSharpRecord then yield "record"
            if v.IsFSharpUnion then yield "union"
            if v.IsInterface then yield "interface"
            if v.IsMeasure then yield "measure"
            // if v.IsProvided then yield "provided"
            // if v.IsStaticInstantiation then yield "static_inst"
            // if v.IsProvidedAndErased then yield "erased"
            // if v.IsProvidedAndGenerated then yield "generated"
            if v.IsUnresolved then yield "unresolved"
            if v.IsValueType then yield "valuetype"

        | :? FSharpMemberOrFunctionOrValue as v ->
            yield "owner: " + (tryOr (fun () -> v.EnclosingEntity.CompiledName) "<unknown>")
            if v.IsActivePattern then yield "active_pattern"
            if v.IsDispatchSlot then yield "dispatch_slot"
            if v.IsModuleValueOrMember && not v.IsMember then yield "val"
            if v.IsMember then yield "member"
            if v.IsProperty then yield "property"
            if v.IsExtensionMember then yield "extension_member"
            if v.IsPropertyGetterMethod then yield "property_getter"
            if v.IsPropertySetterMethod then yield "property_setter"
            if v.IsEvent then yield "event"
            if v.EventForFSharpProperty.IsSome then yield "property_event"
            if v.IsEventAddMethod then yield "event_add"
            if v.IsEventRemoveMethod then yield "event_remove"
            if v.IsTypeFunction then yield "type_func"
            if v.IsCompilerGenerated then yield "compiler_gen"
            if v.IsImplicitConstructor then yield "implicit_ctor"
            if v.IsMutable then yield "mutable"
            if v.IsOverrideOrExplicitInterfaceImplementation then yield "override_impl"
            if not v.IsInstanceMember then yield "static"
            if v.IsInstanceMember && not v.IsInstanceMemberInCompiledCode && not v.IsExtensionMember then yield "funky"
            if v.IsExplicitInterfaceImplementation then yield "interface_impl"
            yield sprintf "%A" v.InlineAnnotation
            // if v.IsConstructorThisValue then yield "ctorthis"
            // if v.IsMemberThisValue then yield "this"
            // if v.LiteralValue.IsSome then yield "literal"
        | _ -> () ]

let rec printFSharpDecls prefix decls = seq {
    let mutable i = 0
    for decl in decls do
        i <- i + 1
        match decl with
        | FSharpImplementationFileDeclaration.Entity (e, sub) ->
            yield sprintf "%s%i) ENTITY: %s %A" prefix i e.CompiledName (attribsOfSymbol e)
            if not (Seq.isEmpty e.Attributes) then
                yield sprintf "%sattributes: %A" prefix (Seq.toList e.Attributes)
            if not (Seq.isEmpty e.DeclaredInterfaces) then
                yield sprintf "%sinterfaces: %A" prefix (Seq.toList e.DeclaredInterfaces)
            yield ""
            yield! printFSharpDecls (prefix + "\t") sub
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (meth, args, body) ->
            yield sprintf "%s%i) METHOD: %s %A" prefix i meth.CompiledName (attribsOfSymbol meth)
            yield sprintf "%stype: %A" prefix meth.FullType
            yield sprintf "%sargs: %A" prefix args
            // if not meth.IsCompilerGenerated then
            yield sprintf "%sbody: %A" prefix body
            yield ""
        | FSharpImplementationFileDeclaration.InitAction (expr) ->
            yield sprintf "%s%i) ACTION" prefix i
            yield sprintf "%s%A" prefix expr
            yield ""
}

let printFableDecls decls = seq {
    for decl in decls do
        yield sprintf "%A" decl
}
