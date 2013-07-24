// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open FSharp.Actor
open FSharp.Actor.ZeroMq
open FsCoreSerializer

do
  ActorSystem.configure(
        ActorSystemConfiguration.Create("node-2",
                transports = [ZeroMQ.transport "tcp://127.0.0.1:6667" "tcp://127.0.0.1:6666" [] (new FsCoreSerializer())]
                ))

let pingPong = 
    ActorSystem.actorOf("ping-pong", 
       (fun (actor:Actor) ->
            let log = actor.Log
            let rec loop() = 
                async {
                    let! msg = actor.ReceiveEnvelope()
                    log.Debug(sprintf "Msg: %A %A" msg.Message msg.Sender, None)
                    return! loop()
                }
            loop()
        ))

[<EntryPoint>]
let main argv =
    Console.ReadLine() |> ignore
   
    pingPong <-- "Hello"

    let mutable ended = false
    while not <| ended do
        "ping-pong" ?<-- "Ping"
        let input = Console.ReadLine()
        ended <- input = "exit"

    Console.ReadLine() |> ignore

    0
