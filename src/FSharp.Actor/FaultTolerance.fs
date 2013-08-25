namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging
       
#if INTERACTIVE
open FSharp.Actor
#endif

[<AbstractClass>]
type FaultHandler(?maxFailures, ?minFailureTime) = 
    let mutable state = Map.empty<string, FailureStats>
    let maxFailures = defaultArg maxFailures 10L
    let minFailureTime = defaultArg minFailureTime (TimeSpan.FromMinutes(1.))
    
    abstract Strategy : ActorContext * ActorRef * exn -> unit

    member x.Handle(receiver:ActorContext, child:ActorRef, err:exn) =
         let stats = 
            match state.TryFind(child.Path) with
            | Some(stats) -> stats
            | None -> 
               let stats = FailureStats.Create(child.Path, 1L, DateTimeOffset.UtcNow)
               state <- Map.add child.Path stats state
               stats                  
         match stats.InWindow(maxFailures, minFailureTime) with
         | true -> 
            CallContext.LogicalSetData("actor", receiver.Ref)
            x.Strategy(receiver,child,err)
         | _ -> child.Post(Stop, receiver.Ref |> Some)
         receiver.EventStream.Publish(stats)

module SupervisorStrategy =

    let AlwaysFail = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                originator <-- Stop
        }

    let FailAll = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                receiver.Children |> Seq.iter((-->) Stop)
        }

    let OneForOne decider = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                let response : SupervisorResponse = (decider err)
                originator <-- response
        }
       
    let OneForAll decider = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                for target in (originator :: (receiver.Children |> Seq.toList)) do
                  let response : SupervisorResponse = (decider err)
                  target <-- response
        }



