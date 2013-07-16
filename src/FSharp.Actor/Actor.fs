namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types

type ActorStatus = 
    | NotStarted
    | Running
    | Stopped of string
    | Disposed
    | Faulted of exn
    | Restarting of string
    with
        member x.IsShutdownState() = 
            match x with
            | Stopped(_) -> true
            | Disposed -> true
            | _ -> false

type SystemMessage = 
    | Shutdown of string
    | Restart of string
    | Link of ActorRef
    | UnLink of ActorRef
    | Watch of ActorRef
    | UnWatch

and SupervisorMessage =
    | Errored of exn * ActorRef

and ActorOptions<'a> = {
    Mailbox : IMailbox<MessageEnvelope>
    Path : ActorPath
    Supervisor : ActorRef option
    OnStartup : (Actor<'a> -> unit) list
    OnShutdown : (Actor<'a> -> unit) list
    OnRestart : (Actor<'a> -> unit) list
    Children : ActorRef list
    Status : ActorStatus
}
with 
    static member Create(?path, ?mailbox, ?children, ?supervisor, ?startupPolicy, ?shutdownPolicy, ?restartPolicy) = 
        {
            Mailbox = defaultArg mailbox (new UnboundedInMemoryMailbox<MessageEnvelope>())
            Path = (defaultArg path (Guid.NewGuid().ToString()))
            OnStartup = defaultArg shutdownPolicy [(fun (_:Actor<'a>) -> ())]
            OnShutdown = defaultArg shutdownPolicy [(fun (_:Actor<'a>) -> ())]
            OnRestart = defaultArg restartPolicy [(fun (_:Actor<'a>) -> ())]
            Supervisor = supervisor
            Children = defaultArg children []
            Status = ActorStatus.NotStarted
        }
    static member Default = ActorOptions<'a>.Create()

and Actor<'a> internal(computation : Actor<'a> -> Async<unit>, ?options) as self =
    
    let mutable cts = new CancellationTokenSource()  
    let mutable options = defaultArg options (ActorOptions<_>.Create())
    let stateChangeSync = new obj()

    let updateOptions f =
        options <- (f options)

    let rec run (actor:Actor<'a>) = 
       updateOptions (fun o -> { o with Status = ActorStatus.Running})
       async {
             try
                do! computation actor
                return shutdown actor (ActorStatus.Stopped("graceful shutdown"))
             with e ->
                do! handleError actor e
       }

    and handleError (actor:Actor<'a>) (err:exn) =
        updateOptions (fun o -> { o with Status = ActorStatus.Faulted(err) })
        async {
            match actor.Options.Supervisor with
            | Some(sup) -> sup <-- Errored(err, actor.Ref)
            | None -> 
                do Logger.Current.Error(sprintf "%A errored - shutting down" actor, Some err)
                do shutdown actor (ActorStatus.Faulted(err))
        }

    and shutdown (actor:Actor<'a>) status =
        lock stateChangeSync (fun _ ->
            cts.Cancel()
            updateOptions (fun o -> { o with Status = status })
            cts <- null
            List.iter (fun f -> f(actor)) actor.Options.OnShutdown
            )

    and start (actor:Actor<_>) = 
        lock stateChangeSync (fun _ ->
            cts <- new CancellationTokenSource()
            Async.Start(run actor, cts.Token)
        )

    and restart (actor:Actor<_>) reason =
        lock stateChangeSync (fun _ ->
            updateOptions (fun o -> { o with Status = ActorStatus.Restarting(reason) })
            cts.Cancel()
            cts <- null
            List.iter (fun f -> f(actor)) actor.Options.OnRestart
            start actor 
        )

    let ref = 
       lazy
           new ActorRef(options.Path, self.Post)

    do
        start self
   
    override x.ToString() = ref.Value.Path

    member x.Log with get() = Logger.Current
    member x.Options with get() = options
    
    member x.ReceiveActorMessage(?timeout, ?token) = 
        options.Mailbox.Receive(timeout, defaultArg token cts.Token)

    member x.Receive<'a>(?timeout, ?token) = 
        async {
           let! msg = x.ReceiveActorMessage(?timeout = timeout, ?token = token)
           return msg.Message |> unbox<'a>
        }

    member x.Post(msg : MessageEnvelope) =
        if not <| x.Options.Status.IsShutdownState()
        then
            if not <| (x.Options.Status = ActorStatus.NotStarted)
            then
               match msg.Message with
               | :? 'a as msg -> options.Mailbox.Post(MessageEnvelope.Create(msg, x.Ref.Path))
               | :? SystemMessage as sysMsg ->
                   match sysMsg with
                   | SystemMessage.Shutdown(reason) -> shutdown x (ActorStatus.Stopped(reason))
                   | SystemMessage.Restart(reason) -> restart x reason
                   | SystemMessage.Link(actor) ->  updateOptions (fun o -> { o with Children =  actor :: o.Children })
                   | SystemMessage.UnLink(actor) ->  updateOptions (fun o -> { o with Children = List.filter ((<>) actor) o.Children })
                   | SystemMessage.Watch(sup) -> 
                        options <- { x.Options with Supervisor = Some(sup) } 
                        sup <-- Link(x.Ref)
                   | SystemMessage.UnWatch -> 
                        match x.Options.Supervisor with
                        | Some(sup) -> 
                            options <- { x.Options with Supervisor = None } 
                            sup <-- UnLink(x.Ref)
                        | None -> ()
               | :? MessageEnvelope as msg -> 
                    options.Mailbox.Post(msg)
               | msg -> 
                 raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot deliver messages of type %A" x (msg.GetType()))    
            else ()
        else raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot deliver messages invalid status %A" x x.Options.Status)

    member x.Ref with get() = ref.Value
    member x.Dispose() = shutdown x (ActorStatus.Disposed)

module Actor =               
   
    ///Creates an actor ref
    let ref (actor:Actor<_>) =
        actor.Ref
    
    ///Creates an actor
    let create options computation = 
        let actor = new Actor<_>(computation, options)
        actor |> ref
        
    ///Links a collection of actors to a parent
    let link (linkees:seq<ActorRef>) (actor:ActorRef) =
        Seq.iter (fun a -> actor.Post(Link(a))) linkees
        actor
    
    ///Creates an actor that is linked to a set of existing actors as it children
    let createLinked options linkees computation =
        link linkees (create options computation)
        
    ///Unlinks a set of actors from their parent.
    let unlink linkees (actor:ActorRef) =
        linkees |> Seq.iter (fun l-> actor.Post(UnLink(l)))
        actor

module Supervisor =

    module Strategy =

        let Forward (target:ActorRef) = 
            (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
               target <-- Errored(err, originator)
            )

        let AlwaysFail = 
            (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
                originator <-- Shutdown("Supervisor:AlwaysFail")
            )

        let FailAll = 
            (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
                reciever.Options.Children 
                |> List.iter ((-->) (Shutdown("Supervisor:FailAll")))
            )

        let OneForOne = 
           (fun (reciever:Actor<_>) err (originator:ActorRef) -> 
                originator <-- Restart("Supervisor:OneForOne")
           )
           
        let OneForAll = 
           (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
                reciever.Options.Children 
                |> List.iter ((-->) (Restart("Supervisor:OneForAll")))
           )

    let private defaultHandler maxFailures strategy (actor:Actor<SupervisorMessage>)  =
        let rec supervisorLoop (restarts:Map<string,int>) = 
            async {
                let! msg = actor.Receive()
                match msg with
                | Errored(err, targetActor) ->
                    match restarts.TryFind(targetActor.Path), maxFailures with
                    | Some(count), Some(maxfails) when count < maxfails -> 
                        strategy actor err targetActor                            
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | Some(count), Some(maxfails) -> 
                        targetActor.Post(SystemMessage.Shutdown("Too many restarts"))                          
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | Some(count), None -> 
                        strategy actor err targetActor                              
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | None, Some(maxfails) ->
                        strategy actor err targetActor                              
                        return! supervisorLoop (Map.add targetActor.Path 1 restarts)
                    | None, None ->
                        strategy actor err targetActor                                
                        return! supervisorLoop (Map.add targetActor.Path 1 restarts)            
            }
        supervisorLoop Map.empty

    let create path strategy comp = 
        Actor.create (ActorOptions<SupervisorMessage>.Create(path)) (comp strategy)
    
    let createDefault path strategy maxFails = 
        create path strategy (defaultHandler maxFails)

    let superviseAll (children:seq<ActorRef>) (supervisor:ActorRef) = 
        children |> Seq.iter ((-->) (Watch(supervisor)))
        supervisor
