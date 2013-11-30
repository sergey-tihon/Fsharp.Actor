namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorPath = string

type ActorRef = 
    | Remote of IActorTransport * ActorPath
    | Local of IActor
    | Null
    member x.Path 
        with get() = 
           match x with
           | Remote(t, path) -> t.Scheme + "://" + path
           | Local(actor) -> "local://" + actor.Name
           | Null -> String.Empty

and Message<'a> = {
    Sender : ActorRef
    Target : ActorRef
    Message : 'a
}

and IActorTransport = 
    abstract Scheme : string with get
    abstract Post : Message<obj> -> unit

and IActor = 
    abstract Name : ActorPath with get
    abstract Post : obj * ActorRef -> unit

type IActor<'a> = 
    inherit IActor
    abstract Post : 'a * ActorRef -> unit

type ActorMessage<'a> =
    | Shutdown 
    | Restart
    | SetSupervisor of ActorRef
    | Link of ActorRef
    | Unlink of ActorRef
    | Msg of Message<'a>

type SupervisorMessage = 
    | Errored of exn

type ActorContext = {
    Current : ActorRef
    Sender : ActorRef
    Children : ActorRef list
}

type ActorDefinition<'a> = {
    Path : ActorPath
    MaxQueueSize : int
    Supervisor : ActorRef
    Behaviour : Behaviour<ActorContext * 'a>
}

type SupervisorResponse = 
    | Stop
    | Continue 

type ErrorContext = {
    Error : exn
    Children : ActorRef list
}





