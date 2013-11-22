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
#load "ActorSystem.fs"

open FSharp.Actor

let system = ActorSystem("calculator")              

type Op = 
    | Op of string * (float * float)
    | Result of string * float

let inline op f = 
    let rec op' (ctx:ActorContext) (left : float, right: float) = 
        async {
           if left > 10. || right > 10.
           then raise(System.OverflowException())
           else ctx <-- Result(string ctx.Ref.Path, f left right)
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
    |> List.map (fun (name, f) -> system.Register(name, f))

let calculatorRef = 
    system.Register("dispatcher",  calculator, (fun config -> 
            { config with 
                SupervisorStrategy = SupervisorStrategy.OneForOne (function 
                                                                   | :? System.OverflowException -> Restart
                                                                   | _ -> Stop)
            }))
    |> Actor.link operations


calculatorRef <-- Op("calculator/add", (5.,7.))
calculatorRef <-- Op("calculator/add", (15.,7.))