namespace FSharp.Actor 

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types

type DispatcherErrorMessage = 
    | StartException of exn
    | ShutdownException of exn
    | MessageHandlingException of exn * MessageEnvelope
    | MessageUndeliverable of MessageEnvelope

type DisruptorBasedDispatcher() = 
    
    let mutable registry : IRegister = Unchecked.defaultof<_>
    let mutable transports : seq<ITransport> = Seq.empty
    let mutable supervisor : ActorRef = Unchecked.defaultof<_>

    let genericHandler handleF = 
        { new Disruptor.IEventHandler<MessageEnvelope> with
               member x.OnNext(data, sequence, endOfBatch) =
                    handleF(data)
        }

    let errorHandler = 
        { new Disruptor.IExceptionHandler with
            member x.HandleOnStartException(error) = 
                 supervisor <-- StartException(error)
            member x.HandleOnShutdownException(error) = 
                 supervisor <-- ShutdownException(error)
            member x.HandleEventException(error, sequence, data) =
                 supervisor <-- MessageHandlingException(error, data :?> MessageEnvelope)
        }

    let mutable router : Disruptor.Dsl.Disruptor<MessageEnvelope> = null

    let publish payload = 
        router.PublishEvent(fun msg _ -> 
                              msg.Message <- payload.Message
                              msg.Target <- payload.Target
                              msg.Sender <- payload.Sender
                              msg.Properties <- payload.Properties
                              msg 
                           )

    let resolveAndPost (msg:MessageEnvelope) = 
        match registry.TryResolve msg.Target with
        | Some(r) -> r.Post(msg)
        | None -> supervisor <-- MessageUndeliverable(msg)

    let wireTransportReceiversAndGetPublishers (transports:seq<ITransport>) =
         transports 
         |> Seq.map (fun transport -> transport.Receive |> Event.add publish; publish)
         |> Seq.toList

    let createRouter() = 
        let transportHandlers = wireTransportReceiversAndGetPublishers transports
        let disruptor = Disruptor.Dsl.Disruptor(MessageEnvelope.Factory, 1048576, Tasks.TaskScheduler.Default)
        disruptor.HandleExceptionsWith(errorHandler)
        disruptor.HandleEventsWith(List.map genericHandler (resolveAndPost :: transportHandlers) |> List.toArray) |> ignore
        disruptor.Start() |> ignore

    interface IDispatcher with
        member x.Post(msg) = publish msg
        member x.Dispose() = 
            router.Halt()
            router <- null
            transports |> Seq.iter (fun t -> t.Dispose())
        member x.Configure(reg:IRegister, ts:seq<ITransport>, sup:ActorRef) = 
            registry <- reg
            transports <- ts
            supervisor <- sup
            createRouter()
