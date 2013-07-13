namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types


 type UnableToDeliverMessageException(msg) = 
     inherit Exception(msg)

 and ActorOptions<'a> = {
     Mailbox : IMailbox<ActorMessage>
     Path : ActorPath
     OnShutdown : (Actor<'a> -> unit)
     OnRestart : (Actor<'a> -> unit)
     OnError : (Actor<'a> -> exn -> ActorRef -> unit)
 }
 with 
     static member Create(?path, ?mailbox, ?errorPolicy, ?shutdownPolicy, ?restartPolicy) = 
         {
             Mailbox = defaultArg mailbox (new UnboundedInMemoryMailbox<ActorMessage>())
             Path = (defaultArg path (Guid.NewGuid().ToString()))
             OnShutdown = defaultArg shutdownPolicy (fun _ -> ())
             OnRestart = defaultArg restartPolicy (fun _ -> ())
             OnError = defaultArg errorPolicy  (fun _ _ _ -> ())
         }
     static member Default = ActorOptions<_>.Create()
 
 and Actor<'a> internal(computation : Actor<'a> -> Async<unit>, ?options) as self =
     
     let mutable status = ActorStatus.Shutdown("Not yet started")
     let mutable cts = new CancellationTokenSource()  
     let mutable options = defaultArg options (ActorOptions<_>.Create())
     let children = new ResizeArray<ActorRef>()
     let stateChangeSync = new obj()
 
     let setStatus newStatus =
         status <- newStatus
 
     let rec run (actor:Actor<'a>) = 
        setStatus ActorStatus.Running
        async {
              try
                 do! computation actor
                 return shutdown actor (ActorStatus.Shutdown("graceful shutdown"))
              with e ->
                 do! handleError actor e
        }

     and handleError (actor:Actor<'a>) (err:exn) =
         setStatus <| ActorStatus.Errored(err)
         async {
             do actor.Options.OnError actor err actor.Ref
             return ()
         }
 
     and shutdown (actor:Actor<'a>) status =
         lock stateChangeSync (fun _ ->
             cts.Cancel()
             setStatus status
             cts <- null
             actor.Options.OnShutdown(actor)
             )
 
     and start (actor:Actor<_>) reason = 
         lock stateChangeSync (fun _ ->
             cts <- new CancellationTokenSource()
             Async.Start(run actor, cts.Token)
         )
 
     and restart (actor:Actor<_>) reason =
         lock stateChangeSync (fun _ ->
             setStatus ActorStatus.Restarting
             cts.Cancel()
             setStatus status
             cts <- null
             actor.Options.OnRestart(actor)
             start actor reason 
         )

     let ref = 
        lazy
            new ActorRef(options.Path, self.Post)
 
     member x.Log with get() = Logger.Current
     member x.Options with get() = options
     member x.Status with get() = status
     member x.Children with get() = children :> seq<_>
     
     member x.ReceiveActorMessage(?timeout, ?token) = 
         options.Mailbox.Receive(timeout, defaultArg token cts.Token)

     member x.Receive<'a>(?timeout, ?token) = 
         async {
            let! msg = x.ReceiveActorMessage(?timeout = timeout, ?token = token)
            return msg.Message |> unbox<'a>
         }

     member x.Start() = start x "Initial Startup"
     member x.Post(msg : obj) =
         if not <| status.IsShutdownState()
         then 
             match msg with
             | :? SystemMessage as sysMsg ->
                 match sysMsg with
                 | Shutdown(reason) -> shutdown x (ActorStatus.Shutdown(reason))
                 | Restart(reason) -> restart x reason
                 | Link(actor) -> children.Add(actor)
                 | UnLink(actor) -> children.Remove(actor) |> ignore
                 | ActorErrored(err, sender) -> x.Options.OnError x err sender
             | :? ActorMessage as msg -> options.Mailbox.Post(msg)      
             | :? 'a as msg -> options.Mailbox.Post(ActorMessage.Create(msg))
             | other -> raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages of type %A expected %A" x (other.GetType()) (typeof<'a>))
         else raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages invalid status %A" x status)
    
     member x.Ref with get() = ref.Value
     member x.Dispose() = shutdown x (ActorStatus.Disposed)

module Actor =               
    
    //registers the actor so it can be resolved by its path
    let register actor = 
        Registry.Actor.register actor
    
    //removes the actor for the registry. This will prevent lookup by path
    let unregister actor = 
        Registry.Actor.unregister actor
    
    ///Creates an actor ref
    let ref (actor:Actor<_>) =
        actor.Ref

    ///Creates an actor
    let create options computation = 
        let actor = new Actor<_>(computation, options)
        actor.Start()
        actor |> ref

    ///Creates an actor and registers it so it can be made discoverable
    let spawn options computation =
        create options computation
        |> register
    
    ///Links a collection of actors to a parent
    let link (linkees:seq<ActorRef>) (actor:ActorRef) =
        Seq.iter (fun a -> actor.Post(Link(a))) linkees
        actor
    
    ///Creates an actor that is linked to a set of existing actors as it children
    let createLinked options linkees computation =
        link linkees (create options computation)
    
    ///Creates an actor that is linked to a set of existing actors as it children, additionally
    ///the parent actor is made discoverable
    let spawnLinked options linkees computation =
        link linkees (spawn options computation)
    
    ///Unlinks a set of actors from their parent.
    let unlink linkees (actor:ActorRef) =
        linkees |> Seq.iter (fun l-> actor.Post(UnLink(l)))
        actor

 module ErrorStrategies = 
     
     let Forward target = 
         (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
            target <-- ActorErrored(err, originator)
         )

     let AlwaysFail = 
         (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
             originator <-- SystemMessage.Shutdown("SupervisorStrategy:AlwaysFail")
         )

     let OneForOne = 
        (fun (reciever:Actor<_>) err (originator:ActorRef) -> 
             originator <-- SystemMessage.Restart("SupervisorStrategy:OneForOne")
        )
        
     let OneForAll = 
        (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
             reciever.Children 
             |> Seq.iter (fun c -> c <-- SystemMessage.Restart("SupervisorStrategy:OneForAll"))
        )

