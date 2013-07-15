namespace FSharp.Actor 

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types


type DisruptorBasedDispatcher(actors, ?handlers, ?onError, ?onUndeliverable) = 
 
    let actorTrie : Trie.trie<string, ActorRef> ref = ref Trie.empty

    let computeKeys (inp:ActorPath) = 
        let segs = inp.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
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

    let onError = defaultArg onError (fun (err,data) -> Logger.Current.Error("A unhandled exception occured in the dispatcher", Some err))
    let onUndeliverable = defaultArg onUndeliverable (fun _ -> ())

    let genericHandler handleF = 
        { new Disruptor.IEventHandler<MessageEnvelope> with
               member x.OnNext(data, sequence, endOfBatch) =
                    handleF(data)
        }

    let errorHandler = 
        { new Disruptor.IExceptionHandler with
            member x.HandleOnStartException(error) = 
                 onError(error, None)
            member x.HandleOnShutdownException(error) = 
                 onError(error, None)
            member x.HandleEventException(error, sequence, data) =
                 onError(error, Some data)
        }

    let resolveAndPost (msg:MessageEnvelope) = 
        match tryFind msg.Target with
        | Some(r) -> r.Post(msg)
        | None -> onUndeliverable(msg)

    let createRouter handlers = 
        let disruptor = Disruptor.Dsl.Disruptor(MessageEnvelope.Factory, 1024, Tasks.TaskScheduler.Default)
        disruptor.HandleExceptionsWith(errorHandler)
        disruptor.HandleEventsWith(List.map genericHandler (resolveAndPost :: handlers) |> List.toArray) |> ignore
        disruptor
    
    let mutable router = null

    let init() = 
        Seq.iter register actors
        router <- createRouter (defaultArg handlers [])
        router.Start() |> ignore

    let publish payload = 
        router.PublishEvent(fun msg _ -> 
                              msg.Message <- payload.Message
                              msg.Target <- payload.Target
                              msg.Sender <- payload.Sender
                              msg.Properties <- payload.Properties
                              msg 
                           )
    do 
        init()
    

    interface IDispatcher with
        member x.Post(msg) = publish msg
        member x.Dispose() = 
            router.Halt()
            router <- null
            actorTrie := Trie.empty
        member x.Register(ref) = register(ref)
        member x.Remove(ref) = unregister(ref)
        member x.Resolve(path) = find(path)
        member x.ResolveAll(path) = findAll(path) |> Seq.ofList

module Dispatcher = 

    let mutable private dispatcher : IDispatcher option =  None 

    let set(s) =
        dispatcher |> Option.iter (fun s -> s.Dispose())
        dispatcher <- Some s 
        
    let get() = 
        match dispatcher with
        | Some(d) -> d
        | None -> raise(InvalidDispatchConfiguration("A dispatcher instance must be set")) 
    
    let postWithSender path msg sender = get().Post <| MessageEnvelope.Create(msg, path, sender)
    
    let post path msg =  get().Post <| MessageEnvelope.Create(msg, path)
    
    let postMessage msg = get().Post <| msg

    let broadcast paths msg = paths |> Seq.iter (fun r -> post r msg)

[<AutoOpen>]
module Operators =

    let (!!) (path:ActorPath) = Dispatcher.get().Resolve path
    let (?<--) (path:ActorPath) msg = Dispatcher.post path msg
    let (?<-*) (refs:ActorPath) (msg:'a) = Dispatcher.get().ResolveAll refs |> Seq.iter (fun r -> r.Post(msg))
    let (<-*) (refs:seq<ActorRef>) (msg:'a) = refs |> Seq.iter (fun r -> r.Post(msg))
