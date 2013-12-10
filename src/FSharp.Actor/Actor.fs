namespace FSharp.Actor

open System
open System.Threading
#if INTERACTIVE
open FSharp.Actor
#endif

module Actor = 

    let fromDefinition config = 
        (new Actor<_>(config))

    let create name handler = 
        actor {
            path name
            messageHandler handler
        } |> fromDefinition

    let ref actor = Local actor

    let unType<'a> (actor:IActor<'a>) = 
        (actor :?> Actor<'a>) :> IActor

    let reType<'a> (actor:IActor) = 
        (actor :?> Actor<'a>) :> IActor<'a>

    let register ref = ref |> unType |> ActorSystem.Register

    let spawn config = 
        config |> (fromDefinition >> register)