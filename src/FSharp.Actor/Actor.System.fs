namespace FSharp.Actor

open System
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorSystem internal() = 
    static let eventStream = ref Unchecked.defaultof<IEventStream>
    static let localTransport = new LocalTransport()
    static let transports : Map<string, IActorTransport> ref =
        [
            "local", localTransport  :> IActorTransport
        ] |> Map.ofList |> ref

    static member private RegisterTransports (transprts:seq<IActorTransport>) = 
        Seq.iter (fun (transport:IActorTransport) ->
                    transports := Map.add transport.Scheme transport !transports
                ) transprts

    static member Register (actor:IActor) = 
        localTransport.Register(actor)
        actor
    
    static member Unregister (actor:IActor) = 
        localTransport.UnRegister(actor)

    static member TryResolveTransport(scheme:string) = 
        Map.tryFind scheme !transports

    static member LocalTransport = localTransport
    static member EventStream = !eventStream
    static member Configure(?evntStream,?transports:seq<IActorTransport>) = 
        eventStream := (defaultArg evntStream (new DefaultEventStream() :> IEventStream))
        ActorSystem.RegisterTransports(defaultArg transports Seq.empty)

