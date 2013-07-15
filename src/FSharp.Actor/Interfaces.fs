namespace FSharp.Actor

open System
open System.Threading

[<AutoOpen>]
module Types = 
    
    type ActorNotFound(message) = 
        inherit Exception(message)

    type UnableToDeliverMessageException(msg) = 
        inherit Exception(msg)

    type InvalidDispatchConfiguration(msg) =
        inherit Exception(msg)

    type ActorPath = string
                
    type MessageEnvelope = { 
        mutable Message : obj
        mutable Properties : Map<string, obj>
        mutable Target : ActorPath
        mutable Sender : ActorPath option
    }
    with 
        static member Factory =
            new Func<_>(fun () -> { Message = null; Properties = Map.empty; Sender = None; Target = null } )
        static member Create(message, target, ?sender, ?props) = 
            { Message = box message; Properties = defaultArg props Map.empty; Sender = sender; Target = target } 
 
    type ActorRef(path:ActorPath, postF: MessageEnvelope -> unit) =
         member val Path = path with get
         member x.Post(msg:'a) = MessageEnvelope.Create(msg, x.Path) |> postF 
         member x.Post(msg, ?sender, ?props) = MessageEnvelope.Create(msg, x.Path, ?sender = sender, ?props = props) |> postF
         member x.Post(msg:MessageEnvelope) = msg |> postF
        
         override x.ToString() =  x.Path
         override x.Equals(y:obj) = 
             match y with
             | :? ActorRef as y -> x.Path = y.Path
             | _ -> false
         override x.GetHashCode() = x.Path.GetHashCode()
         static member (<--) (ref:ActorRef,msg:'a) = ref.Post(msg)
         static member (<--) (ref:ActorRef,(msg:'a, sender)) = ref.Post(msg, sender)
         static member (<--) (ref:ActorRef,(msg:'a, sender, props)) = ref.Post(msg, sender, props)
         static member (<--) (ref:ActorRef, msg) = ref.Post(msg)
         static member (-->) (msg:'a, ref:ActorRef) = ref.Post(msg)
         static member (-->) ((msg:'a, sender), ref:ActorRef) = ref.Post(msg, sender)
         static member (-->) ((msg:'a, sender, props),ref:ActorRef) = ref.Post(msg, sender, props)
         static member (-->) (msg, ref:ActorRef) = ref.Post(msg)
      

    type IDispatcher = 
        inherit IDisposable
        abstract Post : MessageEnvelope -> unit
        abstract Resolve : ActorPath -> ActorRef 
        abstract ResolveAll : ActorPath -> seq<ActorRef> 
        abstract Register : ActorRef -> unit
        abstract Remove : ActorRef -> unit  

    type ILogger = 
        abstract Debug : string * exn option -> unit
        abstract Info : string * exn option -> unit
        abstract Warning : string * exn option -> unit
        abstract Error : string * exn option -> unit

    type IReplyChannel<'a> =
        abstract Reply : 'a -> unit
    
    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit

    type IRegistry = 
        inherit IDisposable

