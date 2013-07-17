namespace FSharp.Actor

type SystemMessage = 
    | Shutdown of string
    | Restart of string
    | Link of ActorRef
    | UnLink of ActorRef
    | Watch of ActorRef
    | UnWatch 

type SupervisorMessage =
    | Errored of exn * ActorRef

