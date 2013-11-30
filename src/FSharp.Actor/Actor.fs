namespace FSharp.Actor

open System
#if INTERACTIVE
open FSharp.Actor
#endif

module Actor = 

    let create config = 
        Actor(config) :> IActor<_>

    let spawn config = 
        config |> (create >> register)