namespace FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif
open FSharp.Actor

module Supervisor = 
    
    let create config exceptionHandler =
        actor {
            inherits config
            messageHandler (fun (ctx:ActorContext<SupervisorMessage>) ->
                let rec loop() =
                    async {
                        let! msg = ctx.Receive()
                        match msg.Message with
                        | SupervisorMessage.Errored(error) -> 
                            let! (result : SystemMessage) = exceptionHandler { Error = error; Children = ctx.Children }
                            msg.Sender |> post <| result
                            return! loop()
                    } 
                loop()   
            )
        } |> Actor.create



