namespace FSharp.Actor

open System
open FSharp.Actor

type LocalTransport() =
    
    let actorTrie : Trie.trie<string, ActorRef> ref = ref Trie.empty

    let computeKeys (inp:ActorPath) = 
        let segs = inp.Actor.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
        segs |> Array.map (fun x -> x.Replace(":", "")) |> List.ofArray
    
    let findAll address = 
        Trie.subtrie (computeKeys address) !actorTrie |> Trie.values 

    let find address = 
        match findAll address with
        | [] -> raise(ActorNotFound(sprintf "Could not find actor %A" address))
        | a -> a |> List.head

    let tryFind address = 
        match findAll address with
        | [] -> None
        | a -> a |> List.head |> Some

    let register (actor:ActorRef) = 
        actorTrie := Trie.add (computeKeys actor.Path) actor !actorTrie
    
    let unregister (actor:ActorRef) = 
        actorTrie := Trie.remove (computeKeys actor.Path) !actorTrie
    
    let recieveEvent = new Event<MessageEnvelope>()

    interface ITransport with
        member x.Descriptor with get() = Transport("local", Environment.MachineName, None)
        member x.Register(ref) = register(ref)
        member x.Remove(ref) = unregister(ref)
        member x.Get(path) = find(path)
        member x.TryGet(path) = tryFind(path)
        member x.GetAll(path) = findAll(path) |> Seq.ofList
        member x.Post(msg) = 
            match tryFind msg.Target with
            | Some(ref) -> ref.Post(msg)
            | None -> ()
        member x.Start() = ()
        member x.Receive with get() = recieveEvent.Publish
        member x.Dispose() = 
                actorTrie := Trie.empty

