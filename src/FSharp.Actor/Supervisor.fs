namespace FSharp.Actor

open System
open FSharp.Actor
open FSharp.Actor.Types

module Supervisor = 

    type Message = 
        | ActorErrored of exn * IActor

    type Strategy = (exn -> IActor -> IActor -> unit)

    module Strategies = 
        
        let AlwaysFail : Strategy = 
            (fun err (supervisor:IActor) (target:IActor) -> 
                target <-- SystemMessage.Shutdown("SupervisorStrategy:AlwaysFail")
            )

        let OneForOne : Strategy = 
           (fun err (supervisor:IActor) (target:IActor) -> 
                target <-- SystemMessage.Restart("SupervisorStrategy:OneForOne")
           )
           
        let OneForAll : Strategy = 
           (fun err (supervisor:IActor) (target:IActor) -> 
                (supervisor :?> Actor<_>).Children 
                |> Seq.iter (fun c -> c <-- SystemMessage.Restart("SupervisorStrategy:OneForAll"))
           )


    let private defaultHandler maxFailures strategy (supervisor:Actor<_>) =
            let rec supervisorLoop (restarts:Map<string,int>) = 
                async {
                    let! msg = supervisor.Receive()
                    match msg with
                    | ActorErrored(err, targetActor) ->
                        match restarts.TryFind(targetActor.Path.AbsoluteUri), maxFailures with
                        | Some(count), Some(maxfails) when count < maxfails -> 
                            strategy err supervisor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Path.AbsoluteUri (count + 1) restarts)
                        | Some(count), Some(maxfails) -> 
                            targetActor <-- SystemMessage.Shutdown("Too many restarts")                          
                            return! supervisorLoop (Map.add targetActor.Path.AbsoluteUri (count + 1) restarts)
                        | Some(count), None -> 
                            strategy err supervisor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Path.AbsoluteUri (count + 1) restarts)
                        | None, Some(maxfails) ->
                            strategy err supervisor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Path.AbsoluteUri 1 restarts)
                        | None, None ->
                            strategy err supervisor targetActor                            
                            return! supervisorLoop (Map.add targetActor.Path.AbsoluteUri 1 restarts)                 
                }
            supervisorLoop Map.empty


    let spawn (options:ActorContext<Message>) supervisorLoop = 
        Actor.spawn options supervisorLoop 

    let create (options:ActorContext<_>) supervisorLoop = 
        Actor.create options supervisorLoop 

    let spawnDefault options strategy maxfails = 
        spawn options (defaultHandler maxfails strategy)

    let createDefault options strategy maxfails = 
        create options (defaultHandler maxfails strategy)

    let superviseAll (actors:seq<IActor>) sup = 
        actors |> Seq.iter (fun a -> a <-- Watch(sup))
        sup

