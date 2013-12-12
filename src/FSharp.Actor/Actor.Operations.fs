namespace FSharp.Actor

open System
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module Operations =

    let private getSenderRef() = 
        let sender = CallContext.LogicalGetData("actor")
        match sender with
        | null -> Null
        | :? IActor as actor -> Local(actor)
        | _ -> failwithf "Unknown sender ref %A" sender

    let resolve (target:ActorPath) =
        match Uri.TryCreate(target, UriKind.RelativeOrAbsolute) with
        | true, uri -> 
            if uri.IsAbsoluteUri && (uri.Scheme <> "local")
            then
                match ActorSystem.TryResolveTransport(uri.Scheme) with
                | Some(t) ->
                    Remote(t, uri.AbsoluteUri)
                | None -> Null
            else
                let path = 
                    if uri.IsAbsoluteUri
                    then uri.Host + "/" + uri.PathAndQuery
                    else target
                ActorSystem.LocalTransport.Resolve (path.TrimEnd([|'/'|]))
        | _ -> failwithf "Not a valid ActorPath %s" target

    let post (target:ActorRef) (msg:'a) = 
        let sender = getSenderRef()
        match target with
        | Remote(transport, path) -> transport.Post({ Target = target; Sender = sender; Message = msg })
        | Local(actor) -> actor.Post(msg, sender)
        | Null -> ()
    
    let inline (!!) path = resolve path
    let inline (<--) target msg = post target msg
    let inline (-->) msg target = post target msg


