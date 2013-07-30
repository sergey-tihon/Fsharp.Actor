namespace FSharp.Actor

open System
open System.Reflection
open FSharp.Actor

type ActorSystemAlreadyConfigured() =
     inherit Exception("ActorSystem is already configured.")

type ActorSystemNotConfigured() =
    inherit Exception("An ActorSystem must be configured. Make a call to ActorSystem.configure")

type ActorSystemConfiguration = {
    Name : string
    Transports : ITransport list
}
with
    static member Default = ActorSystemConfiguration.Create("default-system")
    static member Create(name, ?transports) =
        {
            Name = name 
            Transports = (defaultArg transports [])
        } 


type ActorSystem(config:ActorSystemConfiguration) =
    
    let localTransport = (new LocalTransport() :> ITransport)
    let allTransports = localTransport :: config.Transports

    let bindTransport (transport:ITransport) = 
        transport.Receive 
        |> Event.add (fun x -> 
                if x.Target.System = config.Name || x.Target.System = "*"
                then localTransport.Post(x))
        transport.Start()

    do
        config.Transports 
        |> Seq.iter bindTransport

    member x.GetAll(path) =
        allTransports |> Seq.choose (fun t -> t.TryGet path) 

    member x.Post path msg = 
        x.GetAll path |> Seq.iter (fun r -> r.Post(MessageEnvelope.Create(msg, path)))

    member x.ActorOf(name:string, computation, ?options) =
         let actor = (new Actor(ActorPath.Create(name, config.Name), computation, ?options = options)).Ref
         localTransport.Register(actor)
         actor

    interface IDisposable with
       member x.Dispose() = 
            config.Transports |> Seq.iter (fun x -> x.Dispose())

type Node() = 
    
    static let systems = ref Map.empty<string, ActorSystem> 

    static member Configure(configurations) = 
        systems := 
            Seq.fold (fun s config -> 
                        Map.add config.Name (new ActorSystem(config)) s
                     ) (!systems) configurations

    static member System
        with get(indexer) : ActorSystem = 
            (!systems).[indexer]

    static member All() = 
        (!systems) |> Map.toSeq |> Seq.map snd
    
    static member Shutdown() = 
        (!systems) |> Map.toSeq |> Seq.map snd |> Seq.iter (fun s -> (s :> IDisposable).Dispose())

[<AutoOpen>]
module Operators =

    let (!!) (path:string) =
        let path = ActorPath.Create(path)
        match path.System with
        | "*" -> 
            Node.All() |> Seq.collect (fun sys -> sys.GetAll(path))
        | sys -> 
            Node.System(sys).GetAll(path)
        |> Seq.toArray

    let (<--) (refs:seq<ActorRef>) msg = 
        refs |> Seq.iter(fun x -> x.Post(MessageEnvelope.Create(msg, x.Path)))

    let (-->) msg (refs:seq<ActorRef>)  = 
        refs |> Seq.iter(fun x -> x.Post(MessageEnvelope.Create(msg, x.Path)))