open System
open FSharp.Actor
open FSharp.Actor.ZeroMq
open FsCoreSerializer

do
  ActorSystem.Configure(
    transports = [
        new ZeroMqTransport(Uri("tcp://127.0.0.1:6666"), Uri("tcp://127.0.0.1:6667"), serializer = new FsCoreSerializer())
  ])

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
   
    !!"zeromq://ping-pong" <-- "Hello from node-1"

    Console.ReadLine() |> ignore

    0