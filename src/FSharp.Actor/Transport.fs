namespace FSharp.Actor

open System
open FSharp.Actor

type LocalTransport() = 
     let actors : Trie.trie<string, IActor> ref = ref Trie.empty

     let computeKeysFromPath (path:string) = 
         path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

     let find address = 
        Trie.subtrie (computeKeysFromPath address) !actors |> Trie.values

     member x.Register (actor:IActor) = 
         actors := Trie.add (computeKeysFromPath (string actor.Name)) actor !actors
     
     member x.UnRegister (actor:IActor) =  
         actors := Trie.remove (computeKeysFromPath actor.Name) !actors

     member x.Resolve address = 
         match find address with
         | [] -> Null
         | h :: _ -> Local h
     
     member x.UnRegister address =
         actors := Trie.remove (computeKeysFromPath (string address)) !actors



     interface IActorTransport with
         member x.Scheme with get() = "local"
         member x.Post(msg) =
            match msg.Target with
            | Local(actor) -> actor.Post(msg.Message, msg.Sender)
            | _ -> ()
         member x.Dispose() =
            actors := Trie.empty
            