namespace FSharp.Actor

open System
open System.Threading
#if INTERACTIVE
open FSharp.Actor
#endif

[<RequireQualifiedAccess>]
module Actor = 

    let fromDefinition config = new Actor<_>(config)

    let create name handler = 
        actor {
            path name
            messageHandler handler
        } |> fromDefinition

    let ref actor = Local actor

    let sender() = Operations.getSenderRef()

    let resolve path = Operations.resolve path

    let unType<'a> (actor:IActor<'a>) = 
        (actor :?> Actor<'a>) :> IActor

    let reType<'a> (actor:IActor) = 
        (actor :?> Actor<'a>) :> IActor<'a>

    let register ref = ref |> unType |> ActorSystem.Register

    let spawn config = 
        config |> (fromDefinition >> register)

[<AutoOpen>]
module ActorOperators = 
    let inline (!!) path = resolve path
    let inline (<--) target msg = post target msg
    let inline (-->) msg target = post target msg