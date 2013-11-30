namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging

[<AutoOpen>]
module Types =
    
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
        abstract Publish : 'a -> unit
        abstract Publish : string * 'a -> unit
        abstract Subscribe<'a> : ('a -> unit) -> unit
        abstract Subscribe : string * (Event -> unit) -> unit
        abstract Unsubscribe<'a> : unit -> unit
        abstract Unsubscribe : string -> unit

    type ActorEvents = 
        | ActorStarted of ActorRef
        | ActorShutdown of ActorRef
        | ActorRestart of ActorRef
        | ActorErrored of ActorRef * exn
        | ActorAddedChild of ActorRef * ActorRef
        | ActorRemovedChild of ActorRef * ActorRef
    
    type MessageEvents = 
        | Undeliverable of obj * Type * Type * ActorRef option 