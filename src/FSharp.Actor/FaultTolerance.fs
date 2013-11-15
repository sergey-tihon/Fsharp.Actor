namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging
       
#if INTERACTIVE
open FSharp.Actor
#endif

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



