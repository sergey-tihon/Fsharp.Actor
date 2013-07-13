namespace FSharp.Actor 

open FSharp.Actor.Types

module Registry = 
 
    open System

    type ActorNotFound(message) = 
        inherit Exception(message)

    module Actor = 

        let private actors : Trie.trie<string, ActorRef> ref = ref Trie.empty

        let private computeKeys (inp:ActorPath) = 
            let segs = inp.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
            segs |> Array.map (fun x -> x.Replace(":", "")) |> List.ofArray

        let all() = Trie.values !actors

        let clear() = 
            actors := Trie.empty
        
        let findUnderPath (address : ActorPath) =
            Trie.subtrie (computeKeys address) !actors |> Trie.values 

        let find address = 
            match findUnderPath address with
            | [] -> raise(ActorNotFound(sprintf "Could not find actor %A" address))
            | a -> a |> List.head

        let tryFind address = 
            match findUnderPath address with
            | [] -> None
            | a -> a |> List.head |> Some

        let remove (address:ActorPath) = 
            actors := Trie.remove (computeKeys address) !actors

        let register (actor:ActorRef) = 
            actors := Trie.add (computeKeys actor.Path) actor !actors
            actor
        
        let unregister (actor:ActorRef) = 
            remove actor.Path

module Dispatcher = 
    
    open System
    open System.Threading

    let private genericHandler handleF = 
        { new Disruptor.IEventHandler<ActorMessage> with
               member x.OnNext(data, sequence, endOfBatch) =
                    handleF(data)
        }



    let mutable private deadLetterQueue = (new DeadLetterMailbox<ActorMessage>(1000)) :> IMailbox<ActorMessage>
    let mutable private onUndeliverable = deadLetterQueue.Post
    let mutable private onError = (fun (err, message:_ option) -> Logger.Current.Error("An error occured when routing message", Some err))
    
    let setUndeliverableStrategy f = 
        onUndeliverable <- f

    let setErrorStrategy f =
        onError <- f

    let private errorHandler = 
        { new Disruptor.IExceptionHandler with
            member x.HandleOnStartException(error) = 
                 onError(error, None)
            member x.HandleOnShutdownException(error) = 
                 onError(error, None)
            member x.HandleEventException(error, sequence, data) =
                 onError(error, Some data)
        }

    let resolveAndPost (msg:ActorMessage) = 
        Option.iter (fun (ref:ActorRef) -> 
                        match Registry.Actor.tryFind ref.Path with
                        | Some(r) -> r.Post(msg)
                        | None -> onUndeliverable(msg)
                    ) msg.Target

    let private createRouter handlers = 
        let disruptor = Disruptor.Dsl.Disruptor(ActorMessage.Factory, 1024, Tasks.TaskScheduler.Default)
        disruptor.HandleExceptionsWith(errorHandler)
        disruptor.HandleEventsWith(List.map genericHandler (resolveAndPost :: handlers) |> List.toArray) |> ignore
        disruptor
    
    let mutable private router = null

    let init(handlers) = 
        router <- createRouter handlers
        router.Start() |> ignore

[<AutoOpen>]
module Operators =
    
    let (!*) path = Registry.Actor.findUnderPath path
    let (!!) path = Registry.Actor.find path
    let (<--) (ref:ActorRef) (msg:'a) = ref.Post(msg)
    let (<-*) refs msg = refs |> Seq.iter ((<--) msg)
    let (?<--) id msg = !*id <-* msg