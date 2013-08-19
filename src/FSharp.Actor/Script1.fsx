#load "Logger.fs"
#load "Mailboxes.fs"
#load "Primitives.fs"
#load "Messages.fs"
#load "FaultTolerance.fs"
#load "Actor.fs"


open FSharp.Actor

type Result = 
    | Result of float
    | Error of exn

let op name f = 
    Actor.create("calculator/" + name, 
        (fun ctx -> 
            let rec loop (ctx:ActorContext) = 
                async {
                    try
                        let! (a,b) = ctx.Receive<float * float>()
                        ctx.Log.Info(sprintf "parent %A" ctx.Parent, None)
                        ctx <-- Result(f a b)
                        return! loop ctx
                    with e -> 
                        ctx <-- Error(e)
                        return! loop ctx
                }
            loop ctx
        ))

type Op = 
    | Add of float * float
    | Multiply of float * float
    | Divide of float * float
    | Subtract of float * float

let calculator = 
    let add = op "add" (+)
    let mul = op "mul" (*)
    let div = op "div" (/)
    let sub = op "sub" (-)
    let calculator =
         Actor.create("calculator", 
            (fun ctx -> 
                let rec doCalc (ctx:ActorContext) = 
                    async { 
                      
                        let! op = ctx.Receive()
                        match op with
                        | Add(a,b) -> add <-- (a,b)
                        | Multiply(a,b) -> mul <-- (a,b)
                        | Divide(a,b) -> div <-- (a,b)
                        | Subtract(a,b) -> sub <-- (a,b)
                        return! awaitResult op ctx
                    }
                and awaitResult op (ctx:ActorContext) = 
                    async {

                        let! result = ctx.Receive()
                        match result with
                        | Result(r) -> ctx.Log.Info(sprintf "Result: %A = %A" op r, None)
                        | Error(err) -> ctx.Log.Error(sprintf "%A errored" op, Some err)
                        return! doCalc ctx  
                    }
                doCalc ctx
            ))

    calculator <-- Link(add)
    calculator <-- Link(mul)
    calculator <-- Link(div)
    calculator <-- Link(sub)
    calculator

calculator <-- Add(5.,7.)