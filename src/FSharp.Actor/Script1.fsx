#r @"..\..\packages\Disruptor-DamageBoy-IH.2.8.0.1\lib\net40\Disruptor.dll"
#r @"..\..\packages\NLog.2.0.1.2\lib\net45\NLog.dll"
#load "Trie.fs"
#load "Types.fs"
#load "Logger.fs"
#load "Mailboxes.fs"
#load "EventStream.fs"
#load "Registry.fs"
#load "Messages.fs"
#load "FaultTolerance.fs"
#load "Actor.fs"

open FSharp.Actor

let eventStream = new EventStream() :> IEventStream
let logger = Logger.create "test"

eventStream.Subscribe(function
                      | ActorStarted(ref) -> logger.Debug("Actor Started {0}",[|ref|], None)
                      | ActorShutdown(ref) -> logger.Debug("Actor Shutdown {0}",[|ref|], None)
                      | ActorRestart(ref) -> logger.Debug(sprintf "Actor Restart {0}",[|ref|], None)
                      | ActorErrored(ref,err) -> logger.Error(sprintf "Actor Errored {0}", [|ref|], Some err)
                      | ActorAddedChild(parent, ref) -> logger.Debug(sprintf "Linked Actors {1} -> {0}",[|parent; ref|], None)
                      | ActorRemovedChild(parent, ref) -> logger.Debug(sprintf "UnLinked Actors {1} -> {0}",[|parent;ref|], None)
                      )

eventStream.Subscribe(function
                      | Undeliverable(msg, expectedType, actualType, target) -> logger.Warning("Couldn't deliver {0} to {1} expected type {2}, but goet actual type {3}", [|msg; target; expectedType; actualType|], None)
                     )
                                     

type Op = 
    | Op of string * (float * float)
    | Result of string * float

let inline op f = 
    let rec op' (ctx:ActorContext) (left : float, right: float) = 
        async {
           if left > 10. || right > 10.
           then raise(System.OverflowException())
           else ctx <-- Result(ctx.Ref.Path, f left right)
           return Receive(op')
        }
    Receive(op')

let calculator =
    let rec calc' (ctx:ActorContext) op = 
               async { 
                   match op with
                   | Op(op, values) -> !!op <-- values
                   | Result(op, r) -> ctx.Log.Info("Result: {0} = {1}",[|op;r|], None)
                   return Receive(calc')
               }
    Receive(calc') 

let operations = 
    ["add", op (+); "mul", op (*); "div", op (/); "sub", op (-)]
    |> List.map (fun (name, f) -> Actor.create(name, eventStream, id, f) |> Actor.register)

let calculatorRef = 
    Actor.create("calculator", eventStream, (fun config -> 
                                               { config with 
                                                   SupervisorStrategy = SupervisorStrategy.OneForOne (function 
                                                                                                      | :? System.OverflowException -> Restart
                                                                                                      | _ -> Stop)
                                               }), calculator)
    |> Actor.link operations


calculatorRef <-- Op("add", (5.,7.))
calculatorRef <-- Op("add", (15.,7.))