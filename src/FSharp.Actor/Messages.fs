namespace FSharp.Actor

open System
#if INTERACTIVE
open FSharp.Actor
#endif

type SystemMessage = 
    | Shutdown of string
    | Errored of exn * ActorRef
    | Parent of ActorRef
    | Link of ActorRef
    | UnLink of ActorRef 

type SupervisorResponse =
    | Stop
    | Restart
    | Resume

type ActorEvents = 
    | ActorStarted of ActorRef
    | ActorShutdown of ActorRef
    | ActorRestart of ActorRef
    | ActorErrored of ActorRef * exn
    | ActorAddedChild of ActorRef * ActorRef
    | ActorRemovedChild of ActorRef * ActorRef

type MessageEvents = 
    | Undeliverable of obj * Type * Type * ActorRef option 

type Metrics = 
    | Failure of FailureStats