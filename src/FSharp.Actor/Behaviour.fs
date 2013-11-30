namespace FSharp.Actor

type Behaviour<'a> = 
    | Behaviour of ('a-> Async<Behaviour<'a>>)


type BehaviourBuilder() = 
    member x.Zero() = Behaviour(fun msg -> async { return x.Zero() })
    member x.Return(f : 'a -> Async<Behaviour<'a>>) = Behaviour(f)

[<AutoOpen>]
module Behaviour = 
    
    let behaviour = new BehaviourBuilder()
    let emptyBehaviour<'a> = 
        behaviour {
            let rec loop (msg:'a) = 
                async {
                    return Behaviour(loop)
                }
            return loop
        }
