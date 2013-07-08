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
    
    and Message<'a> = 
        | Shutdown of string
        | Restart of string
        | Message of 'a * IActor option
        | Link of IActor
        | UnLink of IActor
        | Watch of IActor
        | UnWatch
        | Errored of exn * IActor

    and IActor =
         inherit IDisposable
         abstract Id : string with get
         abstract Path : ActorPath with get
         abstract Post : Message<'a> -> unit
         abstract PostAndTryAsyncReply : (IReplyChannel<'b> -> Message<'a>) * int option -> Async<'b option>
         abstract Status : ActorStatus with get
         abstract Children : seq<IActor> with get
         abstract QueueLength : int with get
         abstract Start : unit -> unit
         [<CLIEvent>] abstract PreStart : IEvent<IActor> with get
         [<CLIEvent>] abstract PreRestart :  IEvent<IActor> with get
         [<CLIEvent>] abstract PreStop :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnStopped :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnStarted :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnRestarted :  IEvent<IActor> with get

    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract PostAndTryAsyncReply : (IReplyChannel<'b> -> 'a) * int option -> Async<'b option>
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit