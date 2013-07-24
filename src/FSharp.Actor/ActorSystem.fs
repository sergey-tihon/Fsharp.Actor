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
    Register : IRegister
    Transports : seq<ITransport>
    Dispatcher : IDispatcher
    Supervisor : ActorRef
}
with
    static member Create(name, ?transports, ?supervisorStrategy, ?register, ?dispatch) = 
        let strategy = defaultArg supervisorStrategy SupervisorStrategy.OneForOne
        {
            Name = name 
            Register = defaultArg register (new TrieBasedRegistry())
            Transports = defaultArg transports []
            Dispatcher = defaultArg dispatch (new DisruptorBasedDispatcher())
            Supervisor = (new Supervisor(ActorPath.Create("supervisor", name), Supervisors.upNfailsPerActor 3 strategy)).Ref
        } 
    member x.Dispose() = 
         x.Register.Dispose()
         x.Dispatcher.Dispose()
         x.Supervisor <-- Shutdown("ActorSystem disposed")

type ActorSystem() =

    static let mutable config = (ActorSystemConfiguration.Create("default-system"))

    static member configure(?configuration:ActorSystemConfiguration) =
        let configuration = defaultArg configuration (ActorSystemConfiguration.Create("default-system"))
        configuration.Dispatcher.Configure(configuration.Register, configuration.Transports, configuration.Supervisor)
        config <- configuration
        Logger.Current.Debug(sprintf "Created ActorSystem %s" config.Name, None)

    static member resolve(path) =
        config.Register.Resolve(path)
      
    static member post path msg = 
        config.Dispatcher.Post(MessageEnvelope.Create(msg, path))
    
    static member actorOf(name:string, computation, ?options) =
         let actor = (new Actor(ActorPath.Create(name, config.Name), computation, ?options = options)).Ref
         config.Register.Register actor
         actor

[<AutoOpen>]
module Operators =

    let (!!) (path:ActorPath) = ActorSystem.resolve path
    let (?<--) (path:string) msg = ActorSystem.post (ActorPath.op_Implicit(path)) msg

