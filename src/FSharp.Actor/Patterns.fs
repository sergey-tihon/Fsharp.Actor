namespace FSharp.Actor

module Patterns =
    
    open FSharp.Actor
    open FSharp.Actor.Types

    let deadLetter path = 
        Actor.spawn (ActorContext.Create(path)) (fun (actor:Actor<_>) -> 
            let rec loop() = 
                async {
                    let! msg = actor.Receive()
                    return! loop()
                }
            loop())

    module Dispatch = 

        let shortestQueue<'a> name (refs : seq<IActor>) =
            Actor.spawn (ActorContext.Create(name)) (fun (actor:Actor<'a>) -> 
                let log = actor.Log
                let rec loop() =
                    async {
                        try
                            let! msg = actor.Receive()
                            (actor.Children |> Seq.minBy (fun x -> (x :?> Actor<'a>).Options.Mailbox.Length)).Post(msg)
                            return! loop()
                        with e -> 
                            log.Error("Error:", Some e)
                            return! loop()
                    }
                loop()
            ) |> Actor.link refs


        let roundRobin<'a> name (refs : IActor[]) =
            Actor.spawn (ActorContext.Create(name)) (fun (actor:Actor<'a>) ->
                let rec loop indx = 
                    async {
                        let! msg = actor.Receive()
                        refs.[indx].Post(msg)
                        return! loop ((indx + 1) % refs.Length)
                    }
                loop 0
            ) |> Actor.link refs

    module Routing =
       
        let route name (router : 'msg -> seq<IActor> option) =
            Actor.spawn name (fun (actor:Actor<'msg>) ->
                let rec loop() =
                    async {
                        let! msg = actor.Receive()
                        match router msg with
                        | Some(targets) -> targets |> Seq.iter ((<--) msg)
                        | None -> ()
                        return! loop()
                    }
                loop()
            )

        let broadcast name (targets:seq<IActor>) =
            Actor.spawn name (fun (actor:Actor<_>) ->
                let rec loop() =
                    async {
                        let! msg = actor.Receive()
                        do targets |> Seq.iter ((<--) msg)
                        return! loop()
                    }
                loop()
            )

    
    let map name (f : 'a -> 'b) (target:IActor) = 
        Actor.spawn name (fun (actor:Actor<'a>) ->
            let rec loop() =
                async {
                    let! msg = actor.Receive()
                    target <-- f msg
                    return! loop()
                }
            loop()
        ) 

    let filter name (f : 'a -> bool) (target:IActor) = 
        Actor.spawn name (fun (actor:Actor<'a>) ->
            let rec loop() =
                async {
                    let! msg = actor.Receive()
                    if (f msg) then target <-- msg
                    return! loop()
                }
            loop()
        )
