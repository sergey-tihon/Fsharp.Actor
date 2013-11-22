namespace FSharp.Actor 

#if INTERACTIVE
open FSharp.Actor
#endif

module Registry = 
 
    open System

    type ActorNotFound(message) = 
        inherit Exception(message)

    let private actors : Trie.trie<string, ActorRef> ref = ref Trie.empty

    let all() = Trie.values !actors

    let clear() = 
        actors := Trie.empty
    
    let private computeKeysFromPath (path:string) = 
        path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    let findUnderPath address =
        Trie.subtrie (computeKeysFromPath address) !actors |> Trie.values

    let find address = 
        match findUnderPath address with
        | [] -> raise(ActorNotFound(sprintf "Could not find actor %A" address))
        | a -> a |> List.head

    let tryFind address = 
        match findUnderPath address with
        | [] -> None
        | a -> a |> List.head |> Some

    let register (actor:ActorRef) = 
        actors := Trie.add (computeKeysFromPath (string actor.Path)) actor !actors
        actor

    let remove address = 
        actors := Trie.remove (computeKeysFromPath address) !actors

[<AutoOpen>]
module Operators =
    
    let inline (!*) id = Registry.findUnderPath id
    let inline (!!) id = Registry.find id

