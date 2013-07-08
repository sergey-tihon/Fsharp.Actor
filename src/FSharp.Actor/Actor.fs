namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types

module Actor = 
    
    type UnableToDeliverMessageException(msg) = 
        inherit Exception(msg)

    type InvalidSupervisorException(msg) = 
        inherit Exception(msg)

    type ShutdownPolicy =
        | Cascade 
        | Selective of (IActor -> bool)
        | Default

    type RestartPolicy = 
        | Cascade 
        | Selective of (IActor -> bool)
        | Default

    type Options<'a> = {
        Id : string
        Mailbox : IMailbox<Message<'a>>
        Supervisor : IActor option
        Logger : ILogger
        Path : ActorPath
        ShutdownPolicy : ShutdownPolicy
        RestartPolicy : RestartPolicy
    }
    with 
        static member Create(?id, ?mailbox, ?supervisor, ?logger, ?address, ?shutdownPolicy, ?restartPolicy) = 
            let logger = defaultArg logger Logging.Console
            let id = defaultArg id (Guid.NewGuid().ToString())
            {
                Id = id
                Mailbox = defaultArg mailbox (new DefaultMailbox<Message<'a>>() :> IMailbox<Message<'a>>)
                Supervisor = supervisor
                Logger = logger
                Path = defaultArg address (Path.create id)
                ShutdownPolicy = defaultArg shutdownPolicy ShutdownPolicy.Default
                RestartPolicy = defaultArg restartPolicy RestartPolicy.Default
            }
        static member Default = Options<'a>.Create()
    
    type T<'a> internal(computation : IActor -> Async<unit>, ?options) as self =
        let mutable status = ActorStatus.Shutdown("Not yet started")
        let mutable cts = new CancellationTokenSource()  
        let mutable options = defaultArg options (Options<'a>.Create())
        let preStart = new Event<IActor>()
        let preRestart = new Event<IActor>()
        let preStop = new Event<IActor>()
        let onStopped = new Event<IActor>()
        let onRestarted = new Event<IActor>()
        let onStart = new Event<IActor>()
        let children = new ResizeArray<IActor>()
        let stateChangeSync = new obj()
    
        let setStatus newStatus = 
            status <- newStatus
    
        let rec run (actor:IActor) = 
           setStatus ActorStatus.Running
           async {
                 try
                    do! computation actor
                    return shutdown actor (ActorStatus.Shutdown("graceful shutdown"))
                 with e ->
                    do! handleError actor e
           }

        and handleError (actor:IActor) (err:exn) =
            setStatus <| ActorStatus.Errored(err)
            async {
                match options.Supervisor with
                | Some(supervisor) -> 
                    supervisor.Post(Errored(err, actor))
                | None ->
                    shutdown actor (ActorStatus.Shutdown(sprintf "An exception was handled\r\n%A" err))
                return ()
            }
    
        and shutdown (actor:IActor) status =
            lock stateChangeSync (fun _ ->
                preStop.Trigger(actor)
                cts.Cancel()
                setStatus status
                cts <- null
                match options.ShutdownPolicy with
                | ShutdownPolicy.Default -> ()
                | ShutdownPolicy.Cascade -> 
                       children 
                       |> Seq.iter (fun x -> x <-- Shutdown(sprintf "Parent %s shutdown with status %A" actor.Id status)) 
                | ShutdownPolicy.Selective(predicate) -> 
                       children 
                       |> Seq.filter predicate
                       |> Seq.iter (fun x -> x <-- Shutdown(sprintf "Parent %s shutdown with status %A" actor.Id status)) 
                onStopped.Trigger(actor)
                )
    
        and start (actor:IActor) reason = 
            lock stateChangeSync (fun _ ->
                preStart.Trigger(actor)  
                cts <- new CancellationTokenSource()
                Async.Start(run self, cts.Token)
                onStart.Trigger(actor)
            )
    
        and restart (actor:IActor) reason =
            lock stateChangeSync (fun _ ->
                setStatus ActorStatus.Restarting
                preRestart.Trigger(actor)
                cts.Cancel()
                setStatus status
                cts <- null
                match options.RestartPolicy with
                | RestartPolicy.Default -> ()
                | RestartPolicy.Cascade -> 
                       children 
                       |> Seq.iter (fun x -> x <-- Restart(sprintf "Parent %s restarting" actor.Id)) 
                | RestartPolicy.Selective(predicate) -> 
                       children 
                       |> Seq.filter predicate
                       |> Seq.iter (fun x -> x <-- Restart(sprintf "Parent %s restarting" actor.Id)) 
                start actor reason 
                onRestarted.Trigger(actor)
            )
        
        override x.ToString() =  (x :> IActor).Id
        override x.Equals(y:obj) = 
            match y with
            | :? IActor as y -> (x :> IActor).Id = y.Id
            | _ -> false
        override x.GetHashCode() = (x :> IActor).Id.GetHashCode()
    
        member x.Log with get() = options.Logger
        member x.Options with get() = options
        
        interface IActor with
            
            [<CLIEvent>]
            member x.OnRestarted = onRestarted.Publish
            [<CLIEvent>]
            member x.OnStarted = onStart.Publish
            [<CLIEvent>]
            member x.OnStopped = onStopped.Publish
            [<CLIEvent>]
            member x.PreRestart = preRestart.Publish
            [<CLIEvent>]
            member x.PreStart = preStart.Publish
            [<CLIEvent>]
            member x.PreStop = preStop.Publish
    
            member x.Id with get()= options.Id
            member x.Path with get()= options.Path
            member x.QueueLength with get() = options.Mailbox.Length
            member x.Start() = 
                start self "Initial Startup"

            member x.Post(msg : Message<'a>) =
                if not <| status.IsShutdownState()
                then 
                    match msg with
                    | Message(_,_) as a ->
                        options.Mailbox.Post(msg)
                    | Errored(_,_) as a -> ()
                    | Shutdown(reason) -> shutdown x (ActorStatus.Shutdown(reason))
                    | Restart(reason) -> restart x reason
                    | Link(actor) -> children.Add(actor)
                    | UnLink(actor) -> children.Remove(actor) |> ignore
                    | Watch(supervisor) -> 
                        options <- { options with Supervisor = Some supervisor }
                        supervisor.Post(Link(x))
                    | UnWatch -> 
                         match options.Supervisor with
                         | Some(sup) -> 
                             options <- { options with Supervisor = None }
                             sup.Post(UnLink(x))
                         | None -> () 
                else raise(UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages" status))
    
            member x.PostAndTryAsyncReply(msgf : (IReplyChannel<'b> -> Message<_>), ?timeout) = 
                if not <| status.IsShutdownState()
                then options.Mailbox.PostAndTryAsyncReply(msgf, timeout)
                else raise(UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages" status))

            member x.Children with get() = children :> seq<_>
            member x.Status with get() = status
            member x.Dispose() = shutdown x (ActorStatus.Disposed)
                   

    let logEvents (logger:ILogger) (actor:IActor) =
        actor.OnRestarted |> Event.add (fun a -> logger.Debug(sprintf "%A restarted Status: %A" a a.Status, None))
        actor.OnStarted |> Event.add (fun a -> logger.Debug(sprintf "%A started Status: %A" a a.Status, None))
        actor.OnStopped |> Event.add (fun a -> logger.Debug(sprintf "%A stopped Status: %A" a a.Status, None))
        actor.PreRestart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-restart Status: %A" a a.Status, None))
        actor.PreStart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-start Status: %A" a a.Status, None))
        actor.PreStop |> Event.add (fun a -> logger.Debug(sprintf "%A pre-stop Status: %A" a a.Status, None))
        actor

    let start (actor:IActor) = 
        actor.Start()
        actor
    
    let create (options:Options<_>) computation = 
        (new T<_>(computation, options) :> IActor)
        |> logEvents options.Logger
        

    let register actor = 
        Registry.Actor.register actor

    let unregister actor = 
        Registry.Actor.unregister actor

    let unregisterOnShutdown (actor:IActor) = 
        actor.OnStopped |> Event.add (Registry.Actor.unregister)
        actor

    let spawn (options:Options<_>) computation =
        create options computation
        |> register
        |> unregisterOnShutdown
        |> start

    let supervisedBy sup (actor:IActor) = 
        actor.Post(Watch(sup))
        actor

    let link (linkees:seq<IActor>) (actor:IActor) =
        Seq.iter (fun a -> actor.Post(Link(a))) linkees
        actor

    let createLinked options linkees computation =
        link linkees (create options computation)

    let spawnLinked options linkees computation =
        link linkees (spawn options computation)

    let unlink linkees (actor:IActor) =
        linkees |> Seq.iter (fun (l:IActor) -> actor.Post(UnLink(l)))
        actor

    let unwatch (actors:seq<IActor>) = 
        actors |> Seq.iter (fun a -> a.Post(UnWatch))