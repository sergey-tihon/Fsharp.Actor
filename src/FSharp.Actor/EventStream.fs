namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Reflection

#if INTERACTIVE
open FSharp.Actor
#endif

type EventStream(?size) = 
    let logger = Logger.create (typeof<EventStream>.FullName)
    let disruptor = Disruptor.Dsl.Disruptor(Event.Factory, defaultArg size 1024, Tasks.TaskScheduler.Default)
    let subscriptions = new Dictionary<string, (Event -> unit)>()
    let consumer = 
        { new Disruptor.IEventHandler<Event> with
            member x.OnNext(data, sequence, endOfBatch) =
                match subscriptions.TryGetValue(data.Type) with
                | true, f -> 
                    f(data)
                | false, _ -> ()
        }

    let addSubscription typ f = 
        subscriptions.Add(typ, f)

    let removeSubscription typ = 
        subscriptions.Remove(typ) |> ignore

    let publish typ (payload:'a) = 
        if (box payload) <> null then disruptor.PublishEvent(fun msg seq -> msg.SetPayload(seq, typ, payload))
    
    do
        disruptor.HandleEventsWith([| consumer |]) |> ignore
        disruptor.Start() |> ignore

    interface IEventStream with
        member x.Publish(typ, payload : 'a) = publish typ payload
        member x.Publish(payload : 'a) = publish (typeof<'a>.FullName) payload
        member x.Subscribe(typ, callback) = addSubscription typ callback
        member x.Subscribe<'a>(callback) = addSubscription (typeof<'a>.FullName) (fun event -> event.As<'a>() |> callback)
        member x.Unsubscribe(typ) = removeSubscription typ
        member x.Unsubscribe<'a>() = removeSubscription (typeof<'a>.FullName)