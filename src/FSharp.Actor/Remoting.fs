namespace FSharp.Actor

open System
open System.IO
open FsCoreSerializer

type RemoteMessage = {
   Source : ActorPath
   Destination : ActorPath
   Payload : obj
}

type ITransport = 
    abstract Listen : unit -> IObservable<RemoteMessage>
    abstract Send : RemoteMessage -> unit

module Remoting =

    let serialize (msg:RemoteMessage) =
        use ms = new MemoryStream()
        FsCoreSerializer.Serialize(ms, msg) 
        ms.Position <- 0L
        ms.ToArray()
        
    let deserialize (bytes:byte[]) =
        if bytes.Length = 0
        then None
        else 
            use ms = new MemoryStream(bytes)
            FsCoreSerializer.Deserialize(ms) 
            |> unbox<RemoteMessage> 
            |> Some
       