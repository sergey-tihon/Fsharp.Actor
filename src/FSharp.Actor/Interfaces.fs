namespace FSharp.Actor

open System
open System.Threading

[<AutoOpen>]
module Types = 
    
    type ActorPath = string

    type ILogger = 
        abstract Debug : string * exn option -> unit
        abstract Info : string * exn option -> unit
        abstract Warning : string * exn option -> unit
        abstract Error : string * exn option -> unit
      
    type ActorStatus = 
        | Running
        | Shutdown of string
        | Disposed
        | Errored of exn
        | Restarting
        with
            member x.IsShutdownState() = 
                match x with
                | Shutdown(_) -> true
                | Disposed -> true
                | _ -> false

    type ActorMessage = { 
        mutable Message : obj
        mutable Properties : Map<string, obj>
        mutable Target : ActorRef option
        mutable Sender : ActorRef option
    }
    with 
        static member Factory =
            new Func<_>(fun () -> { Message = null; Properties = Map.empty; Sender = None; Target = None } )
        static member Create(message, ?target, ?sender, ?props) = 
            { Message = box message; Properties = defaultArg props Map.empty; Sender = sender; Target = target } 
       

    and ActorRef(path:ActorPath, postF: ActorMessage -> unit) =
         member val Path = path with get
         member x.Post(msg, ?sender, ?props) = 
                ActorMessage.Create(msg, x, ?sender = sender, ?props = props)
                |> postF
         member x.Post(msg) = msg |> postF
         override x.ToString() =  x.Path
         override x.Equals(y:obj) = 
             match y with
             | :? ActorRef as y -> x.Path = y.Path
             | _ -> false
         override x.GetHashCode() = x.Path.GetHashCode()

    type IReplyChannel<'a> =
        abstract Reply : 'a -> unit

    type SystemMessage = 
        | Shutdown of string
        | Restart of string
        | Link of ActorRef
        | UnLink of ActorRef
        | ActorErrored of exn * ActorRef

    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit