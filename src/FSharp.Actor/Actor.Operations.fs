namespace FSharp.Actor

open System
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module Operations = 
    
    let internal localTransport = new LocalTransport()
    let private transports : Map<string, IActorTransport> ref =
        [
            "local", localTransport  :> IActorTransport
        ] |> Map.ofList |> ref

    let private getSenderRef() = 
        let sender = CallContext.LogicalGetData("actor")
        match sender with
        | null -> Null
        | :? IActor as actor -> Local(actor)
        | _ -> failwithf "Unknown sender ref %A" sender

    let register (actor:IActor) = 
        localTransport.Register(actor)
        actor

    let registerTransport (transport:IActorTransport) = 
        transports := Map.add transport.Scheme transport !transports

    let unregister (actor:IActor) = 
        localTransport.UnRegister(actor)

    let resolve (target:ActorPath) =
        match Uri.TryCreate(target, UriKind.RelativeOrAbsolute) with
        | true, uri -> 
            if uri.IsAbsoluteUri && (uri.Scheme <> "local")
            then
                match Map.tryFind uri.Scheme !transports with
                | Some(t) -> Remote(t, uri.GetLeftPart(UriPartial.Scheme))
                | None -> Null
            else
                localTransport.Resolve target
        | _ -> failwithf "Not a valid ActorPath %s" target

    let post (target:ActorRef) (msg:'a) = 
        let sender = getSenderRef()
        match target with
        | Remote(transport, path) -> transport.Post({ Target = target; Sender = Remote(transport, sender.Path); Message = msg })
        | Local(actor) -> actor.Post(msg, sender)
        | Null -> ()
    
    let inline (!!) path = resolve path
    let inline (<--) target msg = post target msg
    let inline (-->) msg target = post target msg


