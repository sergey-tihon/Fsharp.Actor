namespace FSharp.Actor

open System
open FSharp.Actor


module ActorSystem =
    
    type ActorSystemAlreadyConfigured(name:string) =
         inherit Exception(sprintf "An actor system named %s - is already configured." name)

    type ActorSystemNotConfigured(name:string) =
        inherit Exception(sprintf "An actor system named %s - must be configured. Make a call to ActorSystem.configure" name)

    type private ActorSystem = {
        Name : string
        Register : IRegister
        Transports : seq<ITransport>
        Dispatcher : IDispatcher
        Supervisor : ActorRef
    }
    with
        member x.Dispose() = 
             x.Register.Dispose()
             x.Dispatcher.Dispose()
             x.Supervisor <-- Shutdown("Actor System disposed")
    

    let mutable private configuredSystems = Map.empty<string, ActorSystem>

    let private getSystems(name) = 
        match name with
        | "*" -> Map.toList configuredSystems |> List.map snd
        | _ -> [configuredSystems.[name]]

    let private execute systemName f = 
        f (getSystems(systemName))

    let configure name transports supervisorStrategy register dispatch =
        match configuredSystems.TryFind(name) with
        | Some(sys) -> 
            //raise an exception as it is hard to reason about the impacts of
            //reconfiguring actors in terms of Async Dispatch
            raise(ActorSystemAlreadyConfigured(name))
        | None ->
            let supervisor = Supervisor.createDefault (ActorPath.Create(name, "supervisor")) supervisorStrategy None
            let instance = { Name = name; Supervisor = supervisor; Transports = transports; Register = register; Dispatcher = dispatch}  
            instance.Dispatcher.Configure(register, transports, supervisor)
            configuredSystems <- Map.add name instance configuredSystems

    let createWithDefaultConfiguration name transports = 
        configure name transports 
            Supervisor.Strategy.OneForOne 
            (new TrieBasedRegistry()) 
            (new DisruptorBasedDispatcher())
    

    let resolve path =
        execute path.System (List.map (fun sys -> sys.Register.Resolve(path))) 
      
    let post path msg = 
        execute path.System (List.iter (fun sys -> sys.Dispatcher.Post(MessageEnvelope.Create(msg, path))))
    
    let broadcast paths msg = 
        paths |> List.iter (fun path -> post path msg)

    let createActor options computation =
        let actor = Actor.create options computation
        execute options.Path.System (List.iter (fun sys -> sys.Register.Register actor))
        actor

[<AutoOpen>]
module Operators =

    let (!!) (path:ActorPath) = ActorSystem.resolve path
    let (?<--) (path:ActorPath) msg = ActorSystem.post path msg
    let (?<-*) (refs:seq<ActorPath>) (msg:'a) = ActorSystem.broadcast (refs |> Seq.toList) msg
    let (<-*) (refs:seq<ActorRef>) (msg:'a) = refs |> Seq.iter (fun r -> r.Post(MessageEnvelope.Create(msg, r.Path)))

