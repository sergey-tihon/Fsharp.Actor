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
    | Restarting
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

and SupervisorMessage =
    | Errored of exn * ActorRef

and ActorOptions<'a> = {
    Mailbox : IMailbox<MessageEnvelope>
    Path : ActorPath
    OnStartup : (Actor<'a> -> unit) list
    OnShutdown : (Actor<'a> -> unit) list
    OnRestart : (Actor<'a> -> unit) list
    OnError : (Actor<'a> -> exn -> ActorRef -> unit)
}
with 
    static member Create(?path, ?mailbox, ?errorPolicy, ?startupPolicy, ?shutdownPolicy, ?restartPolicy) = 
        {
            Mailbox = defaultArg mailbox (new UnboundedInMemoryMailbox<MessageEnvelope>())
            Path = (defaultArg path (Guid.NewGuid().ToString()))
            OnStartup = defaultArg shutdownPolicy [(fun (_:Actor<'a>) -> ())]
            OnShutdown = defaultArg shutdownPolicy [(fun (_:Actor<'a>) -> ())]
            OnRestart = defaultArg restartPolicy [(fun (_:Actor<'a>) -> ())]
            OnError = defaultArg errorPolicy  (fun (_:Actor<'a>) err source -> Logger.Current.Error(sprintf "Actor %A errored" source, Some err))
        }
    static member Default = ActorOptions<'a>.Create()

and Actor<'a> internal(computation : Actor<'a> -> Async<unit>, ?options) as self =
    
    let mutable status = ActorStatus.NotStarted
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
                return shutdown actor (ActorStatus.Stopped("graceful shutdown"))
             with e ->
                do! handleError actor e
       }

    and handleError (actor:Actor<'a>) (err:exn) =
        setStatus <| ActorStatus.Faulted(err)
        async {
            do (actor.Options.OnError actor err actor.Ref)
        }

    and shutdown (actor:Actor<'a>) status =
        lock stateChangeSync (fun _ ->
            cts.Cancel()
            setStatus status
            cts <- null
            List.iter (fun f -> f(actor)) actor.Options.OnShutdown
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
            List.iter (fun f -> f(actor)) actor.Options.OnRestart
            start actor reason 
        )

    let ref = 
       lazy
           new ActorRef(options.Path, self.Post)
   
    override x.ToString() = ref.Value.Path

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
    member x.Post(msg : MessageEnvelope) =
        if not <| status.IsShutdownState()
        then
            if not <| (status = ActorStatus.NotStarted)
            then
               match msg.Message with
               | :? SystemMessage as sysMsg ->
                   match sysMsg with
                   | SystemMessage.Shutdown(reason) -> shutdown x (ActorStatus.Stopped(reason))
                   | SystemMessage.Restart(reason) -> restart x reason
                   | SystemMessage.Link(actor) -> children.Add(actor)
                   | SystemMessage.UnLink(actor) -> children.Remove(actor) |> ignore
               | :? SupervisorMessage as supMsg ->
                   match supMsg with
                   | SupervisorMessage.Errored(err, sender) -> x.Options.OnError x err sender
               | :? 'a as msg -> options.Mailbox.Post(MessageEnvelope.Create(msg, x.Ref.Path))
               | _ -> 
                    options.Mailbox.Post(msg)      
            else ()
        else raise <| UnableToDeliverMessageException (sprintf "Actor (%A) cannot receive messages invalid status %A" x status)

    member x.Ref with get() = ref.Value
    member x.Dispose() = shutdown x (ActorStatus.Disposed)

module Actor =               
   
    ///Creates an actor ref
    let ref (actor:Actor<_>) =
        actor.Ref
    
    ///Creates an actor
    let create options computation = 
        let actor = new Actor<_>(computation, options)
        actor.Start()
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


module Error = 
    
    module Strategy =

        let Forward (target:ActorRef) = 
            (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
               target <-- Errored(err, originator)
            )

        let AlwaysFail = 
            (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
                originator <-- Shutdown("Supervisor:AlwaysFail")
            )

        let OneForOne = 
           (fun (reciever:Actor<_>) err (originator:ActorRef) -> 
                originator <-- Restart("Supervisor:OneForOne")
           )
           
        let OneForAll = 
           (fun (reciever:Actor<_>) err (originator:ActorRef)  -> 
                reciever.Children 
                |> Seq.iter ((-->) (Restart("Supervisor:OneForAll")))
           )

module Supervisor =

    let private defaultHandler maxFailures (actor:Actor<SupervisorMessage>)  =
        let rec supervisorLoop (restarts:Map<string,int>) = 
            async {
                let! msg = actor.Receive()
                match msg with
                | Errored(err, targetActor) ->
                    match restarts.TryFind(targetActor.Path), maxFailures with
                    | Some(count), Some(maxfails) when count < maxfails -> 
                        actor.Options.OnError actor err targetActor                            
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | Some(count), Some(maxfails) -> 
                        targetActor.Post(SystemMessage.Shutdown("Too many restarts"))                          
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | Some(count), None -> 
                        actor.Options.OnError actor err targetActor                              
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | None, Some(maxfails) ->
                        actor.Options.OnError actor err targetActor                              
                        return! supervisorLoop (Map.add targetActor.Path 1 restarts)
                    | None, None ->
                        actor.Options.OnError actor err targetActor                                
                        return! supervisorLoop (Map.add targetActor.Path 1 restarts)            
            }
        supervisorLoop Map.empty

    let create path strategy comp = 
        Actor.create (ActorOptions<SupervisorMessage>.Create(path, errorPolicy = strategy)) comp
    
    let createDefault path strategy maxFails = 
        create path strategy (defaultHandler maxFails)

