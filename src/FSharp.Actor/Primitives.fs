namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging
open System.Collections.Generic

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module Primitives = 

    type ActorPath = string

    let private getCtxData<'a> name = 
        match CallContext.LogicalGetData(name) with
        | null -> None
        | a -> unbox<'a> a |> Some 

    [<AbstractClass>]
    type ActorRef(path:ActorPath) =
         static let send (target:ActorRef) msg = 
             target.Post(msg, getCtxData<ActorRef> "actor")

         member val Path = path with get
         abstract Post : 'a * ActorRef option -> unit


         override x.ToString() =  x.Path.ToString()
         override x.Equals(y:obj) = 
             match y with
             | :? ActorRef as y -> x.Path = y.Path
             | _ -> false
         override x.GetHashCode() = x.Path.GetHashCode()
         static member (<--) (target:ActorRef, msg) = send target msg
         static member (-->) (msg:'a, target:ActorRef) = send target msg

    type NullActorRef(path:ActorPath) = 
        inherit ActorRef(path)
        override x.Post(msg, sender) = raise(InvalidOperationException(sprintf "Null Actor Ref %s cannot be posted to" path))

    type ActorContext internal(actor:ActorRef, mailbox : IMailbox, ?parent, ?children) =
        let mutable sender = None
        let mutable parent : ActorRef option = parent

        let startTime = DateTimeOffset.UtcNow
        let children = 
            let d = new Dictionary<ActorPath, ActorRef>()
            defaultArg children Seq.empty
            |> Seq.iter (fun (ref:ActorRef) ->
                            d.Add(ref.Path, ref))
            d
    
        member x.Log = Logger.Current
        member val StartTime = DateTimeOffset.UtcNow with get
        member val LastError : exn option = None with get, set

        member x.Children with get() = children.Values :> seq<ActorRef>
        member internal x.AddChild(ref:ActorRef) = 
            children.Add(ref.Path, ref)
        member internal x.RemoveChild(ref:ActorRef) =
            children.Remove(ref.Path) |> ignore
        member x.Resolve(id) =
            match children.TryGetValue(id) with
            | true, ref -> ref
            | false, _ -> new NullActorRef(id) :> ActorRef
        member x.Parent with get() = parent and internal set(v) = parent <- v
        member x.Ref with get() =  actor
        member x.Sender with get() = sender and internal set(v) = sender <- v
        override x.ToString() = actor.ToString()
        member x.Receive<'a>(?timeout) = mailbox.Receive<'a>(timeout)
        member x.Reply(msg) = x.Sender |> Option.iter (fun (s:ActorRef) -> s.Post(msg,Some x.Ref)) 
        member x.Post(target:ActorRef, msg) = target.Post(msg, Some x.Ref)
        static member (<--) (ctx:ActorContext, msg) = ctx.Reply(msg)

    let (!!) id = 
        match getCtxData<ActorContext> "context" with
        | Some(ctx) -> ctx.Resolve(id)
        | None -> new NullActorRef(id) :> ActorRef
  

