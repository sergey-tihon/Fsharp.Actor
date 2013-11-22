namespace FSharp.Actor

open System
open System.Reflection
open Microsoft.FSharp.Reflection

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorSystem(?systemName:string) = 
    
    let actors : Trie.trie<string, ActorRef> ref = ref Trie.empty
    let systemName = defaultArg systemName (System.Reflection.Assembly.GetEntryAssembly().GetName().Name)
    let eventStream = new EventStream() :> IEventStream
    let logger = Logger.create ("ActorSystem-"+systemName)
    let systemSupervisor =
        let rec supervisorLoop (ctx:ActorContext) (msg:'a) = 
                   async { 
                       return Receive(supervisorLoop)
                   }
        Actor.create({ System = systemName; Name = "supervisor" }, eventStream, 
                      (fun config -> 
                          { config with
                              SupervisorStrategy = SupervisorStrategy.OneForOne (fun _ -> Restart)
                          }
                      ), Receive(supervisorLoop))

    do 
        eventStream.Subscribe(function
                          | ActorStarted(ref) -> logger.Debug("Actor Started {0}",[|ref|], None)
                          | ActorShutdown(ref) -> logger.Debug("Actor Shutdown {0}",[|ref|], None)
                          | ActorRestart(ref) -> logger.Debug(sprintf "Actor Restart {0}",[|ref|], None)
                          | ActorErrored(ref,err) -> logger.Error(sprintf "Actor Errored {0}", [|ref|], Some err)
                          | ActorAddedChild(parent, ref) -> logger.Debug(sprintf "Linked Actors {1} -> {0}",[|parent; ref|], None)
                          | ActorRemovedChild(parent, ref) -> logger.Debug(sprintf "UnLinked Actors {1} -> {0}",[|parent;ref|], None)
                          )
        eventStream.Subscribe(function
                              | Undeliverable(msg, expectedType, actualType, target) -> logger.Warning("Couldn't deliver {0} to {1} expected type {2}, but goet actual type {3}", [|msg; target; expectedType; actualType|], None)
                             )

    member x.Register(name:string, comp:Receive<'a>, ?config) = 
        let actor = Actor.create({ System = systemName; Name = name}, eventStream, defaultArg config id, comp) |> Actor.register
        Actor.link [actor] systemSupervisor |> ignore
        actor
     

