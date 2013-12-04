namespace FSharp.Actor

open System

#if INTERACTIVE
open FSharp.Actor
#endif

type SupervisorResponseException(supervisor:ActorRef, actor:ActorRef, actualError:exn) =
    inherit Exception(sprintf "An exception occured in actor %A while waiting for response from %A" actor supervisor, actualError)
    member val Supervisor = supervisor with get
    member val Actor = actor with get
    member val Error = actualError with get

type ActorActionFailedException(action:string, status:ActorStatus, actor:ActorRef, innerException) = 
    inherit Exception(sprintf "Unable to complete action %s on actor %A, status: %A" action actor status, innerException)

type TransportNotRegistered(scheme:string) = 
    inherit Exception(sprintf "No transport registered with scheme %s" scheme)

type InvalidActorPath(path:ActorPath) = 
    inherit Exception(sprintf "Invalid actor path %s" path)
    


