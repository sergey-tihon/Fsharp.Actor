namespace FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif
open FSharp.Actor

module Supervisor = 
    
    let create config exceptionHandler =
        actor {
            inherits config
            messageHandler (fun inp ->
                let rec loop (ctx:ActorContext, msg) =
                    async {
                        match msg with
                        | SupervisorMessage.Errored(error) -> 
                            let! result = exceptionHandler { Error = error; Children = ctx.Children }
                            match result with
                            | Stop -> ctx.Sender |> post <| ActorMessage.Shutdown
                            | Restart -> ctx.Sender |> post <| ActorMessage.Restart
                            | Continue -> ctx.Sender |> post <| ActorMessage.Continue
                            return Behaviour(loop)
                    } 
                loop inp   
            )
        } |> Actor.create



