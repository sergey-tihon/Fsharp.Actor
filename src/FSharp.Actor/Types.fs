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

    type ActorPath = 
        {
            System: string
            Name: string
        }
        with
            override x.ToString() = 
                x.System + "/" + x.Name

    type Receive<'a> = 
        | Receive of (ActorContext -> 'a -> Async<Receive<'a>>)
        | TimeoutReceive of TimeSpan * (ActorContext -> 'a -> Async<Receive<'a>>)
        | Terminate

    and IMailbox = 
         inherit IDisposable
         abstract Receive : int option -> Async<obj * ActorRef option>
         abstract Post : 'a * (ActorRef option) -> unit
         abstract Length : int with get
    
    and [<AbstractClass>] ActorRef(path:ActorPath) =
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
        
    and ActorContext internal(actor:ActorRef, logger:ILogger, eventStream:IEventStream, ?parent) =
        let mutable sender = None
        let mutable parent : ActorRef option = parent

        let startTime = DateTimeOffset.UtcNow
        let children = new ResizeArray<ActorRef>()
    
        member x.Log = logger
        member x.EventStream = eventStream

        member val StartTime = DateTimeOffset.UtcNow with get
        member val LastError : exn option = None with get, set

        member x.Children with get() = children
        member x.Parent with get() = parent and internal set(v) = parent <- v
        member x.Ref with get() =  actor
        member x.Sender with get() = sender and internal set(v) = sender <- v
        member x.Reply(msg) = x.Sender |> Option.iter (fun (s:ActorRef) -> s.Post(msg,Some x.Ref)) 
        member x.Post(target:ActorRef, msg) = target.Post(msg, Some x.Ref)
        static member (<--) (ctx:ActorContext, msg) = ctx.Reply(msg)

    type SystemMessage = 
        | Shutdown of string
        | Errored of exn * ActorRef
        | Parent of ActorRef
        | Link of ActorRef
        | UnLink of ActorRef 
    
    type SupervisorResponse =
        | Stop
        | Restart
        | Resume
    
    type ActorEvents = 
        | ActorStarted of ActorRef
        | ActorShutdown of ActorRef
        | ActorRestart of ActorRef
        | ActorErrored of ActorRef * exn
        | ActorAddedChild of ActorRef * ActorRef
        | ActorRemovedChild of ActorRef * ActorRef
    
    type MessageEvents = 
        | Undeliverable of obj * Type * Type * ActorRef option 
    
    type FailureStats = {
        ActorPath : string
        mutable TotalFailures : int64
        mutable LastFailure : DateTimeOffset option
    }
    with 
        static member Create(path, ?failures, ?time) = 
            { ActorPath = path; TotalFailures = defaultArg failures 0L; LastFailure = time }
        member x.Inc() = 
            x.TotalFailures <- x.TotalFailures + 1L
            x.LastFailure <- Some DateTimeOffset.UtcNow
        member x.InWindow(maxFailures, minFailureTime) = 
            match x.TotalFailures < maxFailures, x.LastFailure with
            | true, None -> true
            | true, Some(last) -> (DateTimeOffset.UtcNow.Subtract(last)) < minFailureTime 
            | false, _ -> false

    [<AbstractClass>]
    type FaultHandler(?maxFailures, ?minFailureTime) = 
        let mutable state = Map.empty<string, FailureStats>
        let maxFailures = defaultArg maxFailures 10L
        let minFailureTime = defaultArg minFailureTime (TimeSpan.FromMinutes(1.))
        
        abstract Strategy : ActorContext * ActorRef * exn -> unit
    
        member x.Handle(receiver:ActorContext, child:ActorRef, err:exn) =
             let stats = 
                match state.TryFind(string child.Path) with
                | Some(stats) -> stats
                | None -> 
                   let stats = FailureStats.Create(string child.Path, 1L, DateTimeOffset.UtcNow)
                   state <- Map.add (string child.Path) stats state
                   stats                  
             match stats.InWindow(maxFailures, minFailureTime) with
             | true -> 
                CallContext.LogicalSetData("actor", receiver.Ref)
                x.Strategy(receiver,child,err)
             | _ -> child.Post(Stop, receiver.Ref |> Some)
             receiver.EventStream.Publish(stats)

    type Metrics = 
        | Failure of FailureStats

    type ActorOptions = {
        Name : string
        Mailbox : IMailbox
        SupervisorStrategy : FaultHandler
        Parent : ActorRef option
        ReceiveTimeout : int option
        EventStream : IEventStream
    }

    [<AttributeUsage(AttributeTargets.Method)>]
    type ActorDefinition(name:string, ?opts:ActorOptions) = 
        inherit Attribute()
        member val Name = name with get
        member val Options = opts with get


