// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open FSharp.Actor
open FSharp.Actor.ZeroMq
open FsCoreSerializer

do
  Node.Configure [
        ActorSystemConfiguration.Create(
            "node-1",
            [ZeroMQ.transport "tcp://127.0.0.1:6666" "tcp://127.0.0.1:6667" (new FsCoreSerializer())]
        )]

let pingPong = 
    Node.System("node-1").ActorOf("ping-pong", 
       (fun (actor:Actor) ->
            let log = actor.Log
            let rec loop() = 
                async {
                    let! msg = actor.ReceiveEnvelope()
                    log.Debug(sprintf "Actor Msg from %A" msg.Sender, None)
                    return! loop()
                }
            loop()
        ))

[<EntryPoint>]
let main argv =
    Console.ReadLine() |> ignore
   
    pingPong <!- "Hello"

    let refs = !!"ping-pong"

    let mutable ended = false
    while not <| ended do
        refs <-- "Ping node-1"
        let input = Console.ReadLine()
        ended <- input = "exit"

    Console.ReadLine() |> ignore

    0