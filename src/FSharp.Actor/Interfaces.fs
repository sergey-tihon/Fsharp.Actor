namespace FSharp.Actor

open System
open System.Threading

[<AutoOpen>]
module Types = 
    
    type ActorPath = Uri

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

    type IReplyChannel<'a> =
        abstract Reply : 'a -> unit

    and SystemMessage = 
        | Shutdown of string
        | Restart of string
        | Link of IActor
        | UnLink of IActor
        | Watch of IActor
        | Unwatch 

    and IActor =
         inherit IDisposable
         abstract Path : ActorPath with get
         abstract Post : obj -> unit
         abstract Start : unit -> unit
//         abstract Status : ActorStatus with get
//         abstract Children : seq<IActor> with get
//         [<CLIEvent>] abstract PreStart : IEvent<IActor> with get
//         [<CLIEvent>] abstract PreRestart :  IEvent<IActor> with get
//         [<CLIEvent>] abstract PreStop :  IEvent<IActor> with get
//         [<CLIEvent>] abstract OnStopped :  IEvent<IActor> with get
//         [<CLIEvent>] abstract OnStarted :  IEvent<IActor> with get
//         [<CLIEvent>] abstract OnRestarted :  IEvent<IActor> with get
    
    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit