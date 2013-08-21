#load "Logger.fs"
#load "Mailboxes.fs"
#load "Primitives.fs"
#load "Messages.fs"
#load "FaultTolerance.fs"
#load "Actor.fs"


open FSharp.Actor

type Op = 
    | Add of float * float
    | Multiply of float * float
    | Divide of float * float
    | Subtract of float * float
    | Result of string * float
    | Error of exn

let op name f = 
    Actor.create("calculator/" + name,
            let rec loop (ctx:ActorContext) ((left, right) : float * float) = 
                async {
                   ctx.Log.Info(sprintf "parent %A" ctx.Parent, None)
                   ctx <-- Result(name, f left right)
                   return HandleWith(loop)
                }
            HandleWith(loop)
        )

let calculator = 
    let add = op "add" (+)
    let mul = op "mul" (*)
    let div = op "div" (/)
    let sub = op "sub" (-)
    let calculator =
         Actor.create("calculator", 
                let rec doCalc (ctx:ActorContext) op = 
                    async { 
                        match op with
                        | Add(a,b) -> add <-- (a,b)
                        | Multiply(a,b) -> mul <-- (a,b)
                        | Divide(a,b) -> div <-- (a,b)
                        | Subtract(a,b) -> sub <-- (a,b)
                        | Result(op, r) -> ctx.Log.Info(sprintf "Result: %A = %A" op r, None)
                        | Error(err) -> ctx.Log.Error(sprintf "%A errored" op, Some err)
                        return HandleWith(doCalc)
                    }
                HandleWith(doCalc)
            )

    calculator <-- Link(add)
    calculator <-- Link(mul)
    calculator <-- Link(div)
    calculator <-- Link(sub)
    calculator

calculator <-- Add(5.,7.)