namespace FSharp.Actor

open System
#if INTERACTIVE
open FSharp.Actor
#endif

module Actor = 
    
    let create config = 
        (new Actor<_>(config))

    let spawn config = 
        config |> (create >> register)