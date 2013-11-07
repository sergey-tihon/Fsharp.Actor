namespace FSharp.Actor

open System.Runtime.Remoting.Messaging
open System.Threading

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorOptions = {
    Mailbox : IMailbox
    SupervisorStrategy : FaultHandler
    Parent : ActorRef option
    ReceiveTimeout : int option
    EventStream : IEventStream
}
with 
    static member create(eventStream, ?parent, ?supervisor, ?mailbox, ?timeout) = 
        {
            Mailbox = defaultArg mailbox (new Mailbox() :> IMailbox)
            SupervisorStrategy = SupervisorStrategy.AlwaysFail
            Parent = parent
            ReceiveTimeout = timeout
            EventStream = eventStream
        }

type Actor<'a> internal(path:ActorPath, comp, options) as self = 
    inherit ActorRef(path)

    let mutable cts : CancellationTokenSource = null
    let mutable isErrored = false

    let options : ActorOptions = options
    let ctx = new ActorContext(self, Logger.create ("Actor: " + path), options.EventStream, ?parent = options.Parent)
    let initialComputation = comp
    let retroActiveMailbox = ref []

    let copyStoredMessages() = 
        !retroActiveMailbox |> List.rev |> List.iter (options.Mailbox.Post) 
        retroActiveMailbox := []    

    let restart(reason) = 
        copyStoredMessages()
        options.EventStream.Publish(ActorEvents.ActorRestart(self))
        if ctx.LastError.IsSome
        then ctx.Log.Debug("{0} restarted due to {1} Error: {2}",[|self;reason;ctx.LastError.Value.Message|], None)
        else ctx.Log.Debug("{0} restarted due to {1}",[|self;reason|], None)

    let shutdown(reason) = 
        cts.Cancel()
        cts <- null
        options.Mailbox.Dispose()
        options.EventStream.Publish(ActorEvents.ActorShutdown(self))
        if ctx.LastError.IsSome
        then ctx.Log.Debug("{0} shutdown due to {1} Error: {2}",[|self;reason;ctx.LastError.Value.Message|], None)
        else ctx.Log.Debug("{0} shutdown due to {1}",[|self;reason|], None)

    let rec receive (ctx:ActorContext) (comp : Receive<'a>) = 
        async {
            try
                let! (msg, sender) = options.Mailbox.Receive(options.ReceiveTimeout)
                ctx.Sender <- sender
                
                match msg with
                | :? SystemMessage as sysMsg -> 
                    match sysMsg with
                    | Shutdown(reason) -> shutdown()
                    | Link(ref) -> 
                         ref <-- Parent(ctx.Ref)
                         ctx.Children.Add(ref)
                         options.EventStream.Publish(ActorEvents.ActorAddedChild(self, ref))
                         return! receive ctx comp
                    | UnLink(ref) -> 
                        ctx.Children.Remove(ref) |> ignore
                        options.EventStream.Publish(ActorEvents.ActorRemovedChild(self, ref))
                        return! receive ctx comp
                    | Parent(ref) -> 
                        ctx.Parent <- Some(ref)
                        return! receive ctx comp
                    | Errored(err, origin) -> 
                         options.SupervisorStrategy.Handle(ctx, origin, err)
                         return! receive ctx comp
                | :? SupervisorResponse as supMsg -> 
                    match supMsg with
                    | Stop -> shutdown()
                    | Restart -> 
                        ctx.LastError <- None
                        return! receive ctx initialComputation
                    | Resume -> 
                        ctx.LastError <- None
                        return! receive ctx comp
                | :? 'a as msg -> 
                    match comp with
                    | Receive(cont) -> 
                        let! ncont = cont ctx msg
                        match ncont with
                        | Terminate -> shutdown()
                        | ncont -> return! receive ctx ncont
                    | TimeoutReceive(timeout, cont) -> 
                        let ncont = Async.RunSynchronously(cont ctx (unbox<_> msg), timeout.TotalMilliseconds |> int)
                        match ncont with
                        | Terminate -> shutdown()
                        | ncont -> return! receive ctx ncont
                    | Terminate -> shutdown()
                | _ -> 
                    options.EventStream.Publish(MessageEvents.Undeliverable(msg, typeof<'a>, msg.GetType(), Some (self :> ActorRef)))
                    return! receive ctx comp
            with e -> 
                return! errored e
        }

    //TODO: Problem with this is that is could block forever.. Probably need TimeoutReceive 
    and waitForSupervisorResponse() = 
        async {
            let! (msg, sender) = options.Mailbox.Receive(options.ReceiveTimeout)
            ctx.Sender <- sender
            match msg with
            | :? SupervisorResponse as supMsg -> 
              match supMsg with
              | Stop -> shutdown()
              | Restart ->
                  restart("")
                  return! receive ctx initialComputation
              | Resume -> 
                  ctx.LastError <- None
                  copyStoredMessages()
                  return! receive ctx comp
            | otherMessage -> 
                retroActiveMailbox := (msg, sender) :: !retroActiveMailbox //Not sure this is a brillant idea
                return! waitForSupervisorResponse()
        }

    and errored err = 
        async {
            ctx.LastError <- Some err
            options.EventStream.Publish(ActorEvents.ActorErrored(self, err))
            match ctx.Parent with
            | Some(parent) -> 
               parent <-- Errored(err, self)
               return! waitForSupervisorResponse()
            | None -> shutdown()
        }

    let start() = 
        cts <- new CancellationTokenSource()
        Async.Start(async {
                      CallContext.LogicalSetData("actor", self :> ActorRef)
                      return! receive ctx comp
                    }, cts.Token)
        options.EventStream.Publish(ActorEvents.ActorStarted(self))

    do start()

    override x.Post(msg, ?from) = 
            options.Mailbox.Post(msg, from)

module Actor = 
    
    let create(path, eventStream, config, comp) = 
        let actor = Actor<_>(path, comp, config (ActorOptions.create(eventStream))) :> ActorRef
        actor
    
    let register (actor:ActorRef) = 
        Registry.register actor

    let link (linkees:seq<ActorRef>) (source : ActorRef)  = 
        linkees |> Seq.iter (fun x -> source <-- (Link(x)))
        source

    let unlink (linkees :seq<ActorRef>) (source : ActorRef)  =
        linkees |> Seq.iter (fun x -> source <-- (UnLink(x)))
        source