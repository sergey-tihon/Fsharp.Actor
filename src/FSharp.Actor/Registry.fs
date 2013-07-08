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
    
    let (!*) id = Registry.Actor.findUnderPath (Path.create id)
    let (!!) id = Registry.Actor.find (Path.create id)

    let (<-*) refs msg = refs |> Seq.iter (fun (a:IActor) -> a.Post <| Message(msg, None))
    let (<--) (ref:IActor) msg = ref.Post <| Message(msg, None)
    let (?<--) id msg = !*id <-* msg

    let (<->) (ref:IActor) msgf = ref.PostAndTryAsyncReply(msgf, None, None)
    let (?<->) id msgf = !!id <-> msgf
    let (<-!>) (ref:IActor) msgf = ref <-> msgf |> Async.RunSynchronously
    let (?<-!>) id msgf = !!id <-> msgf