namespace FSharp.Actor


#if INTERACTIVE
open FSharp.Actor
#endif

type SystemMessage = 
    | Shutdown of string
    | Errored of exn * ActorRef
    | SetParent of ActorRef
    | RemoveParent of ActorRef
    | Link of ActorRef
    | UnLink of ActorRef

type SupervisorResponse =
    | Stop
    | Restart
    | Resume
    | Forward of ActorRef * exn