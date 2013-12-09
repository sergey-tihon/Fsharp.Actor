namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorPath = string

type IMailbox<'a> = 
    inherit IDisposable
    abstract Post : 'a -> unit
    abstract Scan : int * ('a -> Async<'b> option) -> Async<'b>
    abstract Receive : int -> Async<'a>

type ILogger = 
    abstract Debug : string * obj[] * exn option -> unit
    abstract Info : string * obj[]  * exn option -> unit
    abstract Warning : string * obj[] * exn option -> unit
    abstract Error : string * obj[] * exn option -> unit

type Event() = 
    let mutable payload : obj = null
    let mutable payloadType : string = null
    let mutable seqId = 0L
    member x.Payload with get() = payload
    member x.Type with get() = payloadType
    member x.SeqId with get() = seqId

    member internal x.SetPayload(seq, typ, evntPayload:'a) =
        let pl = evntPayload |> box
        if pl <> null 
        then 
            payload <- pl
            seqId <- seq
            payloadType <- typ
        x

    member x.As<'a>() =
        unbox<'a> x.Payload

    static member Factory =
        new Func<_>(fun () -> new Event())

type IEventStream = 
    inherit IDisposable
    abstract Publish : 'a -> unit
    abstract Publish : string * 'a -> unit
    abstract Subscribe<'a> : ('a -> unit) -> unit
    abstract Subscribe : string * (Event -> unit) -> unit
    abstract Unsubscribe<'a> : unit -> unit
    abstract Unsubscribe : string -> unit

type EventStream = 
    | EventStream of IEventStream
    | Null

type ActorRef = 
    | Remote of IActorTransport * ActorPath
    | Local of IActor
    | Null
    member x.Path 
        with get() = 
           match x with
           | Remote(t, path) -> t.Scheme + "://" + path
           | Local(actor) -> "local://" + actor.Name
           | Null -> String.Empty
    override x.ToString() = x.Path
    interface IDisposable with
        member x.Dispose() =
            match x with
            | Local(actor) -> actor.Dispose()
            | _ -> ()

and Message<'a> = {
    Sender : ActorRef
    Target : ActorRef
    Message : 'a
}

and IActorTransport = 
    inherit IDisposable
    abstract Scheme : string with get
    abstract Post : Message<obj> -> unit

and IActor = 
    inherit IDisposable
    abstract Name : ActorPath with get
    abstract Post : obj * ActorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Name : ActorPath with get
    abstract Post : 'a * ActorRef -> unit

type SupervisorMessage = 
    | Errored of exn

type ActorEvents = 
    | ActorStarted of ActorRef
    | ActorShutdown of ActorRef
    | ActorRestart of ActorRef
    | ActorErrored of ActorRef * exn
    | ActorAddedChild of ActorRef * ActorRef
    | ActorRemovedChild of ActorRef * ActorRef

type MessageEvents = 
    | Undeliverable of obj * Type * Type * ActorRef option 

type ActorStatus = 
    | Running 
    | Errored of exn
    | Stopped

type SystemMessage =
    | Shutdown
    | Restart
    | Link of ActorRef
    | Unlink of ActorRef
    | SetSupervisor of ActorRef

type ActorContext<'a> = {
    Logger : ILogger
    Children : ActorRef list
    Mailbox : IMailbox<Message<'a>>
}
with 
    member x.Receive(?timeout) = 
        async { return! x.Mailbox.Receive(defaultArg timeout Timeout.Infinite) }
    member x.Scan(f, ?timeout) = 
        async { return! x.Mailbox.Scan(defaultArg timeout Timeout.Infinite, f) }

type ActorDefinition<'a> = {
    Path : ActorPath
    EventStream : EventStream
    Supervisor : ActorRef
    Behaviour : (ActorContext<'a> -> Async<unit>)
}

type ErrorContext = {
    Error : exn
    Children : ActorRef list
}





