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
                let rec loop (ctx:ActorContext, msg:SupervisorMessage) =
                    async {
                        match msg with
                        | Errored(error) -> 
                            let! result = exceptionHandler { Error = error; Children = ctx.Children }
                            match result with
                            | Stop -> ctx.Sender |> post <| Shutdown
                            | Continue -> ctx.Sender |> post <| Restart
                            return Behaviour(loop)
                    } 
                loop inp   
            )
        } |> Actor.create



