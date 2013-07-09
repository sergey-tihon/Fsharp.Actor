namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types


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



 type ActorContext<'a> = {
     Mailbox : IMailbox<'a>
     Logger : ILogger
     Path : ActorPath
     ShutdownPolicy : ShutdownPolicy
     RestartPolicy : RestartPolicy
     Supervisor : IActor option
     CancellationToken : CancellationToken
 }
 with 
     static member Create(?path, ?mailbox, ?supervisor, ?logger, ?shutdownPolicy, ?restartPolicy, ?cancellationToken) = 
         let logger = defaultArg logger Logging.Console
         {
             Mailbox = defaultArg mailbox (new DefaultMailbox<'a>() :> IMailbox<'a>)
             Supervisor = supervisor
             Logger = logger
             Path = Path.create (defaultArg path (Guid.NewGuid().ToString()))
             ShutdownPolicy = defaultArg shutdownPolicy ShutdownPolicy.Default
             RestartPolicy = defaultArg restartPolicy RestartPolicy.Default
             CancellationToken = defaultArg cancellationToken Async.DefaultCancellationToken
         }
     static member Default = ActorContext<'a>.Create()
 
 type Actor<'a> internal(computation : Actor<'a> -> Async<unit>, ?options) =
     let mutable status = ActorStatus.Shutdown("Not yet started")
     let mutable cts = new CancellationTokenSource()  
     let mutable options = defaultArg options (ActorContext<'a>.Create())
//     let preStart = new Event<IActor>()
//     let preRestart = new Event<IActor>()
//     let preStop = new Event<IActor>()
//     let onStopped = new Event<IActor>()
//     let onRestarted = new Event<Actor>()
//     let onStart = new Event<IActor>()
     let children = new ResizeArray<IActor>()
     let stateChangeSync = new obj()
 
     let setStatus newStatus = 
         status <- newStatus
 
     let rec run (actor:IActor) = 
        setStatus ActorStatus.Running
        async {
              try
                 do! computation (actor :?> Actor<'a>)
                 return shutdown actor (ActorStatus.Shutdown("graceful shutdown"))
              with e ->
                 do! handleError actor e
        }

     and handleError (actor:IActor) (err:exn) =
         setStatus <| ActorStatus.Errored(err)
         async {
             match options.Supervisor with
             | Some(supervisor) -> 
                 supervisor <-- (err, actor)
             | None ->
                 shutdown actor (ActorStatus.Shutdown(sprintf "An exception was unhandled\r\n%A" err))
             return ()
         }
 
     and shutdown (actor:IActor) status =
         lock stateChangeSync (fun _ ->
         //    preStop.Trigger(actor)
             cts.Cancel()
             setStatus status
             cts <- null
             Registry.Actor.unregister actor
             match options.ShutdownPolicy with
             | ShutdownPolicy.Default -> ()
             | ShutdownPolicy.Cascade -> 
                    children 
                    |> Seq.iter (fun x -> x <-- Shutdown(sprintf "Parent %A shutdown with status %A" actor status)) 
             | ShutdownPolicy.Selective(predicate) -> 
                    children 
                    |> Seq.filter predicate
                    |> Seq.iter (fun x -> x <-- Shutdown(sprintf "Parent %A shutdown with status %A" actor status))
              
         //    onStopped.Trigger(actor)
             )
 
     and start (actor:IActor) reason = 
         lock stateChangeSync (fun _ ->
        //     preStart.Trigger(actor)  
             cts <- new CancellationTokenSource()
             Async.Start(run actor, cts.Token)
        //     onStart.Trigger(actor)
         )
 
     and restart (actor:IActor) reason =
         lock stateChangeSync (fun _ ->
             setStatus ActorStatus.Restarting
         //    preRestart.Trigger(actor)
             cts.Cancel()
             setStatus status
             cts <- null
             match options.RestartPolicy with
             | RestartPolicy.Default -> ()
             | RestartPolicy.Cascade -> 
                    children 
                    |> Seq.iter (fun x -> x <-- Restart(sprintf "Parent %A restarting" actor)) 
             | RestartPolicy.Selective(predicate) -> 
                    children 
                    |> Seq.filter predicate
                    |> Seq.iter (fun x -> x <-- Restart(sprintf "Parent %A restarting" actor)) 
             start actor reason 
       //      onRestarted.Trigger(actor)
         )

     override x.ToString() =  (x :> IActor).Path.AbsoluteUri
     override x.Equals(y:obj) = 
         match y with
         | :? IActor as y -> (x :> IActor).Path = y.Path
         | _ -> false
     override x.GetHashCode() = (x :> IActor).Path.GetHashCode()
 
     member x.Log with get() = options.Logger
     member x.Options with get() = options
     member x.Status with get() = status
     member x.Children with get() = children :> seq<_>
     
     member x.Receive(?timeout, ?token) = 
         options.Mailbox.Receive(timeout, defaultArg token options.CancellationToken)

//     [<CLIEvent>]
//     member x.OnRestarted = onRestarted.Publish
//     [<CLIEvent>]
//     member x.OnStarted = onStart.Publish
//     [<CLIEvent>]
//     member x.OnStopped = onStopped.Publish
//     [<CLIEvent>]
//     member x.PreRestart = preRestart.Publish
//     [<CLIEvent>]
//     member x.PreStart = preStart.Publish
//     [<CLIEvent>]
//     member x.PreStop = preStop.Publish


     interface IActor with
         
         member x.Path with get()= options.Path
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
                     | Watch(supervisor) -> 
                         options <- { options with Supervisor = Some supervisor }
                         supervisor.Post(Link(x))
                     | Unwatch -> 
                          match options.Supervisor with
                          | Some(sup) -> 
                              options <- { options with Supervisor = None }
                              sup.Post(UnLink(x))
                          | None -> ()
                 | :? 'a as msg -> options.Mailbox.Post(msg)
                 | other -> raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages of type %A expected %A" x (other.GetType()) (typeof<'a>))
             else raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages invalid status %A" x status)

         member x.Dispose() = shutdown x (ActorStatus.Disposed)

module Actor =               

//    let logEvents (logger:ILogger) (actor:IActor) =
//        let actor = (actor :?> Actor<_>)
//        actor.OnRestarted |> Event.add (fun a -> logger.Debug(sprintf "%A restarted Status: %A" a a.Status, None))
//        actor.OnStarted |> Event.add (fun a -> logger.Debug(sprintf "%A started Status: %A" a a.Status, None))
//        actor.OnStopped |> Event.add (fun a -> logger.Debug(sprintf "%A stopped Status: %A" a a.Status, None))
//        actor.PreRestart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-restart Status: %A" a a.Status, None))
//        actor.PreStart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-start Status: %A" a a.Status, None))
//        actor.PreStop |> Event.add (fun a -> logger.Debug(sprintf "%A pre-stop Status: %A" a a.Status, None))
//        (actor :> IActor)
    
    let start (actor:IActor) = 
        actor.Start()
        actor
    
    let register actor = 
        Registry.Actor.register actor
    
    let unregister actor = 
        Registry.Actor.unregister actor

    let create (options:ActorContext<_>) computation = 
        (new Actor<_>(computation, options) :> IActor)
      //  |> logEvents options.Logger
        
    let bind (actor:IActor) = 
        actor
        |> register
        |> start

    let spawn (options:ActorContext<_>) computation =
        create options computation
        |> bind
    
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
