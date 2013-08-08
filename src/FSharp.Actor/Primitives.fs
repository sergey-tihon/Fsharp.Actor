namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module Primitives = 

    type ActorPath = string

    [<AbstractClass>]
    type ActorRef(path:ActorPath) =
         static let send (target:ActorRef) msg = 
             let sender = CallContext.LogicalGetData("actor")
             match sender with
             | null ->  target.Post(msg, None)
             | sender -> target.Post(msg, Some (sender :?> ActorRef))

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


    type ActorContext internal(actor:ActorRef, mailbox : IMailbox, ?parent) =
        let mutable sender = None
        let mutable parent : ActorRef option = parent

        let startTime = DateTimeOffset.UtcNow
        let children = new ResizeArray<ActorRef>()
    
        member x.Log = Logger.Current
        member val StartTime = DateTimeOffset.UtcNow with get
        member val LastError : exn option = None with get, set

        member x.Children with get() = children
        member x.Parent with get() = parent and internal set(v) = parent <- v
        member x.Ref with get() =  actor
        member x.Sender with get() = sender and internal set(v) = sender <- v
        
        member x.Receive<'a>(?timeout) = mailbox.Receive<'a>(timeout)
        member x.Reply(msg) = x.Sender |> Option.iter (fun (s:ActorRef) -> s.Post(msg,Some x.Ref)) 
        member x.Post(target:ActorRef, msg) = target.Post(msg, Some x.Ref)
        static member (<--) (ctx:ActorContext, msg) = ctx.Reply(msg)
  

