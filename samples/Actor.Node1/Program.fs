open System
open FSharp.Actor
open FSharp.Actor.ZeroMq
open FsCoreSerializer

let logger = Logger.create "Node-1"

do
  
  ActorSystem.Configure(
    transports = [
        new ZeroMqTransport(Uri("tcp://127.0.0.1:6666"), Uri("tcp://127.0.0.1:6667"), serializer = new FsCoreSerializer())
  ])

  ActorSystem.EventStream.Subscribe(function
             | ActorStarted(ref) -> logger.Debug("Actor Started {0}",[|ref|], None)
             | ActorShutdown(ref) -> logger.Debug("Actor Shutdown {0}",[|ref|], None)
             | ActorRestart(ref) -> logger.Debug("Actor Restart {0}",[|ref|], None)
             | ActorErrored(ref,err) -> logger.Error("Actor Errored {0}", [|ref|], Some err)
             | ActorAddedChild(parent, ref) -> logger.Debug("Linked Actors {1} -> {0}",[|parent; ref|], None)
             | ActorRemovedChild(parent, ref) -> logger.Debug("UnLinked Actors {1} -> {0}",[|parent;ref|], None)
             )

let pingPong = 
    Actor.create "ping-pong" 
       (fun (actor:ActorContext<string>) ->
            let log = actor.Logger
            let rec loop() = 
                async {
                    let! msg = actor.Receive()
                    log.Debug(sprintf "Actor Msg: {0} from {1}",[|msg.Message; msg.Sender|], None)
                    return! loop()
                }
            loop())
    |> Actor.register |> Actor.ref

[<EntryPoint>]
let main argv =
    printfn "Press any key to send a message to node two"
    Console.ReadLine() |> ignore
    
    !!"ping-pong" <-- "Hello from LOCAL"
    !!"zeromq://ping-pong" <-- "Hello from node-1"

    Console.ReadLine() |> ignore

    0