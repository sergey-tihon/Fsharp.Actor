namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types

module Supervisor =

    module Strategy =

        let Forward (target:ActorRef) = 
            (fun (reciever:Actor) err (originator:ActorRef)  -> 
               target <-- Errored(err, originator)
            )

        let AlwaysFail = 
            (fun (reciever:Actor) err (originator:ActorRef)  -> 
                originator <-- Shutdown("Supervisor:AlwaysFail")
            )

        let FailAll = 
            (fun (reciever:Actor) err (originator:ActorRef)  -> 
                reciever.Options.Children 
                |> List.iter ((-->) (Shutdown("Supervisor:FailAll")))
            )

        let OneForOne = 
           (fun (reciever:Actor) err (originator:ActorRef) -> 
                originator <-- Restart("Supervisor:OneForOne")
           )
           
        let OneForAll = 
           (fun (reciever:Actor) err (originator:ActorRef)  -> 
                reciever.Options.Children 
                |> List.iter ((-->) (Restart("Supervisor:OneForAll")))
           )

    let private defaultHandler maxFailures strategy (actor:Actor)  =
        let rec supervisorLoop (restarts:Map<ActorPath,int>) = 
            async {
                
                let! msg = actor.Receive()
                match msg with
                | Errored(err, targetActor) ->
                    match restarts.TryFind(targetActor.Path), maxFailures with
                    | Some(count), Some(maxfails) when count < maxfails -> 
                        strategy actor err targetActor                            
                        return! supervisorLoop (Map.add targetActor.Path (count + 1) restarts)
                    | Some(count), Some(maxfails) -> 
                        targetActor <-- SystemMessage.Shutdown("Too many restarts")                          
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
        Actor.create (ActorOptions.Create(path)) (comp strategy)
    
    let createDefault path strategy maxFails = 
        create path strategy (defaultHandler maxFails)

    let superviseAll (children:seq<ActorRef>) (supervisor:ActorRef) = 
        children |> Seq.iter ((-->) (Watch(supervisor)))
        supervisor

