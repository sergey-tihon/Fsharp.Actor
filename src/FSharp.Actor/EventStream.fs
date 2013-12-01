namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
#if INTERACTIVE
open FSharp.Actor
#endif
open FSharp.Actor.DSL

type EventStream(name) = 
    let logger = Logger.create (typeof<EventStream>.FullName)
    let counter = ref 0L
    let subscriptions = new Dictionary<string, (Event -> unit)>()
    let actor =
        actor {
            path name
            messageHandler (fun inp ->
                let rec loop (ctx:ActorContext, msg:Event) =
                    async {
                       match subscriptions.TryGetValue(msg.Type) with
                       | true, f -> f(msg)
                       | false, _ -> ()
                       return Behaviour(loop)
                    } 
                loop inp   
            )
        } |> (fun c -> new Actor<_>(c) :> IActor<_>)

    let addSubscription typ f = 
        subscriptions.Add(typ, f)

    let removeSubscription typ = 
        subscriptions.Remove(typ) |> ignore

    let publish typ (payload:'a) = 
        if (box payload) <> null 
        then
            let event = Event.Factory.Invoke().SetPayload(Interlocked.Increment(counter), typ, payload)
            actor.Post(event, Null)
    

    interface IEventStream with
        member x.Publish(typ, payload : 'a) = publish typ payload
        member x.Publish(payload : 'a) = publish (typeof<'a>.FullName) payload
        member x.Subscribe(typ, callback) = addSubscription typ callback
        member x.Subscribe<'a>(callback) = addSubscription (typeof<'a>.FullName) (fun event -> event.As<'a>() |> callback)
        member x.Unsubscribe(typ) = removeSubscription typ
        member x.Unsubscribe<'a>() = removeSubscription (typeof<'a>.FullName)
        