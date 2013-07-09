namespace FSharp.Actor 

open FSharp.Actor.Types

module Registry = 
 
    open System

    type ActorNotFound(message) = 
        inherit Exception(message)

    module Actor = 

        
        let private actors : Trie.trie<string, IActor> ref = ref Trie.empty

        let all() = Trie.values !actors

        let clear() = 
            actors := Trie.empty
        
        let findUnderPath (address : ActorPath) =
            Trie.subtrie (Path.keys address) !actors |> Trie.values 

        let find address = 
            match findUnderPath address with
            | [] -> raise(ActorNotFound(sprintf "Could not find actor %A" address))
            | a -> a |> List.head

        let tryFind address = 
            match findUnderPath address with
            | [] -> None
            | a -> a |> List.head |> Some

        let remove (address:Uri) = 
            actors := Trie.remove (Path.keys address) !actors

        let register (actor:IActor) = 
            actors := Trie.add (Path.keys actor.Path) actor !actors
            actor
        
        let unregister (actor:IActor) = 
            remove actor.Path



[<AutoOpen>]
module Operators =
    
    let (!*) path = Registry.Actor.findUnderPath (Path.create path)
    let (!!) path = Registry.Actor.find (Path.create path)

    let (<-*) refs msg = refs |> Seq.iter (fun (a:IActor) -> a.Post <| msg)
    let (<--) (ref:IActor) msg = ref.Post <| msg
    let (?<--) id msg = !*id <-* msg