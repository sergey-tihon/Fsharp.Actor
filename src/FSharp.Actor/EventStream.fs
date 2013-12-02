namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
#if INTERACTIVE
open FSharp.Actor
#endif

type DefaultEventStream() = 
    let logger = Logger.create (typeof<EventStream>.FullName)
    let cts = new CancellationTokenSource()
    let counter = ref 0L
    let mutable mailbox = new DefaultMailbox<Event>() :> IMailbox<_>
    let mutable subscriptions = new Dictionary<string, (Event -> unit)>()
    let rec worker() =
        async {
            let! event = mailbox.Receive(Timeout.Infinite)
            match subscriptions.TryGetValue(event.Type) with
            | true, f -> 
                try f(event) with e -> logger.Error("Error occured handling event {0}", [|event|], Some e)
            | false, _ -> ()
            return! worker()
        }

    let addSubscription typ f = 
        subscriptions.Add(typ, f)

    let removeSubscription typ = 
        subscriptions.Remove(typ) |> ignore

    let publish typ (payload:'a) = 
        if (box payload) <> null 
        then
            let event = Event.Factory.Invoke().SetPayload(Interlocked.Increment(counter), typ, payload)
            mailbox.Post(event)
    do
        Async.Start(worker(), cts.Token)

    interface IEventStream with
        member x.Publish(typ, payload : 'a) = publish typ payload
        member x.Publish(payload : 'a) = publish (typeof<'a>.FullName) payload
        member x.Subscribe(typ, callback) = addSubscription typ callback
        member x.Subscribe<'a>(callback) = addSubscription (typeof<'a>.FullName) (fun event -> event.As<'a>() |> callback)
        member x.Unsubscribe(typ) = removeSubscription typ
        member x.Unsubscribe<'a>() = removeSubscription (typeof<'a>.FullName)
        member x.Dispose() = 
            cts.Cancel()
            mailbox.Dispose()
            mailbox <- Unchecked.defaultof<_>;
            subscriptions.Clear()
            subscriptions <- null



        