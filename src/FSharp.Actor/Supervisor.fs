namespace FSharp.Actor

open FSharp.Actor
open FSharp.Actor.Types

module Supervisor = 

    module Strategy = 
        
        let AlwaysFail = 
            (fun err (supervisor:IActor) (target:IActor) -> 
                target.PostSystemMessage(SystemMessage.Shutdown("SupervisorStrategy:AlwaysFail"), Some supervisor)
            )

        let OneForOne = 
           (fun err (supervisor:IActor) (target:IActor) -> 
                target.PostSystemMessage(SystemMessage.Restart("SupervisorStrategy:OneForOne"), Some supervisor)
           )
           
        let OneForAll = 
           (fun err (supervisor:IActor) (target:IActor) -> 
                supervisor.Children 
                |> Seq.iter (fun c -> 
                               c.PostSystemMessage(
                                   SystemMessage.Restart("SupervisorStrategy:OneForAll"), 
                                           Some supervisor))
           )

    type Options = {
        MaxFailures : int option
        Strategy : (exn -> IActor<SupervisorMessage> -> IActor -> unit)
        ActorOptions : Actor.Options<SupervisorMessage>
    }
    with
        static member Default = Options.Create()
        static member Create(?maxFail, ?strategy, ?actorOptions) = 
            {
                MaxFailures = defaultArg maxFail (Some 10)
                Strategy = defaultArg strategy Strategy.OneForOne
                ActorOptions = defaultArg actorOptions (Actor.Options<SupervisorMessage>.Default)
            }

    let private defaultHandler (options:Options) (actor:IActor<SupervisorMessage>) =
            let rec supervisorLoop (restarts:Map<string,int>) = 
                async {
                    let! (msg, sender) = actor.Receive()
                    match msg with
                    | SupervisorMessage.ActorErrored(err, targetActor) ->
                        match restarts.TryFind(targetActor.Id), options.MaxFailures with
                        | Some(count), Some(maxfails) when count < maxfails -> 
                            options.Strategy err actor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Id (count + 1) restarts)
                        | Some(count), Some(maxfails) -> 
                            targetActor.PostSystemMessage(SystemMessage.Shutdown("Too many restarts"), Some(actor :> IActor))                          
                            return! supervisorLoop (Map.add targetActor.Id (count + 1) restarts)
                        | Some(count), None -> 
                            options.Strategy err actor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Id (count + 1) restarts)
                        | None, Some(maxfails) ->
                            options.Strategy err actor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Id 1 restarts)
                        | None, None ->
                            options.Strategy err actor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Id 1 restarts)                   
                }
            supervisorLoop Map.empty


    let spawn (options:Options) supervisorLoop = 
        Actor.spawn { options.ActorOptions with Id = "supervisor-" + options.ActorOptions.Id } (supervisorLoop options)

    let create (options:Options) supervisorLoop = 
        Actor.create { options.ActorOptions with Id = "supervisor-" + options.ActorOptions.Id } (supervisorLoop options)

    let spawnDefault options = 
        spawn options defaultHandler

    let createDefault options = 
        create options defaultHandler

    let superviseAll (actors:seq<IActor>) sup = 
        actors |> Seq.iter (fun a -> a.Watch(sup))
        sup

