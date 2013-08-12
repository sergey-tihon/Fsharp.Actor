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

let op f = 
    (fun ctx ->
        let rec loop (ctx:ActorContext) = 
            async {
                    let! (a,b) = ctx.Receive<float * float>()
                    if a > 10. || b > 10.
                    then raise(System.OverflowException("Values must be less than 10"))
                    else ctx <-- (f a b)
                    return! loop ctx
            }
        loop ctx)

let calculator = 
        (fun ctx -> 
            let rec doCalc (ctx:ActorContext) = 
                async { 
                    printfn "Waiting for message %A" ctx
                    let! op = ctx.Receive()
                    printfn "MSg : %A" op
                    match op with
                    | Add(a,b) -> !!"add" <-- (a,b)
                    | Multiply(a,b) -> !!"mul" <-- (a,b)
                    | Divide(a,b) -> !!"div" <-- (a,b)
                    | Subtract(a,b) -> !!"sub" <-- (a,b)
                    return! awaitResult op ctx
                }
            and awaitResult op (ctx:ActorContext) = 
                async {
                    let! result = ctx.Receive()
                    ctx.Log.Info(sprintf "Result: %A = %f" op result, None)
                    return! doCalc ctx  
                }
            doCalc ctx
        )

let calc =
    Actor.create(calculator, 
                 "calculator", 
                 supervisorStrategy = SupervisorStrategy.OneForAll(fun ref _ err -> 
                                                                        match err with
                                                                        | :? System.OverflowException as ov -> Restart
                                                                        | _ -> Stop),
                 children = [
                    Actor.create(op (+), "add");
                    Actor.create(op (-), "sub");
                    Actor.create(op (*), "mul");
                    Actor.create(op (/), "div");
                 ])


calc <-- Add(5.,7.)
calc <-- Add(15., 7.)