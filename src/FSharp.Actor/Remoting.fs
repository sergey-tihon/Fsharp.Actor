namespace FSharp.Actor

open System

module Remoting =
    
    type RemoteMessage = {
       Source : ActorPath
       Destination : ActorPath
       Payload : obj
    }
    
    type ITransport = 
        abstract Listen : unit -> IObservable<RemoteMessage>
        abstract Send : RemoteMessage -> unit


