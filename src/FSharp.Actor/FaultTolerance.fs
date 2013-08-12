namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open System.Runtime.Remoting.Messaging

type FailureStats = {
    ActorPath : string
    mutable TotalFailures : int64
    mutable LastFailure : DateTimeOffset option
}
with 
    static member Create(path, ?failures, ?time) = 
        { ActorPath = path; TotalFailures = defaultArg failures 0L; LastFailure = time }
    member x.Inc() = 
        x.TotalFailures <- x.TotalFailures + 1L
        x.LastFailure <- Some DateTimeOffset.UtcNow
    member x.InWindow(maxFailures, minFailureTime) = 
        match x.TotalFailures < maxFailures, x.LastFailure with
        | true, None -> true
        | true, Some(last) -> (DateTimeOffset.UtcNow.Subtract(last)) < minFailureTime 
        | false, _ -> false
        

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

module SupervisorStrategy =

    let AlwaysFail = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                originator <-- Stop
        }

    let Forward (target:ActorRef) = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                target <-- Forward(originator, err)
        } 

    let FailAll = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                receiver.Children |> Seq.iter((-->) Stop)
        }

    let OneForOne decider = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                let response : SupervisorResponse = (decider originator err)
                originator <-- response
        }
       
    let OneForAll decider = 
        { new FaultHandler() with
            member x.Strategy(receiver, originator, err) = 
                for target in receiver.Children do
                  let response : SupervisorResponse = (decider originator target err)
                  target <-- response
        }



