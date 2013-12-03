namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Runtime.Remoting.Messaging
open FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif

type Actor<'a>(config:ActorDefinition<'a>) as self = 
    let mailbox = config.Mailbox
    let systemMailbox = new DefaultMailbox<_>()
    let logger = Logger.create config.Path
    let mutable config = config
    let mutable ctx = { Current = Local(self); Logger = logger; Children = []; Status = Running; Sender = Null }

    let publishEvent event = 
        match config.EventStream with
        | EventStream(es) -> es.Publish(event)
        | EventStream.Null -> ()

    let setStatus status = 
        ctx <- { ctx with Status = status }

    let rec run (Behaviour(msgHandler) as behave) = 
       setStatus(ActorStatus.Running)
       async {
           let! msg = mailbox.Receive(config.ReceiveTimeout)
           match msg with
           | ActorMessage.Shutdown -> return! shutdown()
           | ActorMessage.Restart -> return! restart()
           | ActorMessage.Continue -> return! run behave
           | SetSupervisor(ref) ->
               config <- { config with Supervisor =  ref }
               return! run behave
           | Link(ref) -> 
               ctx <- { ctx with Children = (ref :: ctx.Children) }
           | Unlink(ref) -> 
               ctx <- { ctx with Children = (List.filter ((<>) ref) ctx.Children) }
           | Msg(msg) -> 
               let! result = msgHandler ({ ctx with Sender = msg.Sender}, unbox<'a> msg.Message) |> Async.Catch
               match result with
               | Choice1Of2(next) ->  return! run next
               | Choice2Of2(err) -> 
                   return! handleError behave err 
       }
    
    and restart() = 
        async { 
            publishEvent(ActorEvents.ActorRestart(Local(self)))
            match ctx.Status with
            | Errored(err) -> logger.Debug("{0} restarted due to Error: {1}",[|self;err|], None)
            | _ -> logger.Debug("{0} restarted",[|self|], None)
            return! run config.Behaviour
        }

    and shutdown() = 
        async {
            publishEvent(ActorEvents.ActorShutdown(Local(self)))
            match ctx.Status with
            | Errored(err) -> logger.Debug("{0} shutdown due to Error: {1}",[|self;err|], None)
            | _ -> logger.Debug("{0} restarted",[|self|], None)
            setStatus ActorStatus.Stopped
            return ()
        }

    and handleError msgHandler (err:exn) =
        let rec waitSupervisorResponse() = 
            async {
                try        
                    do! mailbox.Scan(fun msg -> 
                                         match msg with
                                         | ActorMessage.Shutdown -> Some(shutdown())
                                         | ActorMessage.Restart -> Some(restart())
                                         | ActorMessage.Continue -> Some(run msgHandler)
                                         | _ -> None
                                     )
                with e -> 
                    let err = SupervisorResponseException(config.Supervisor, Local(self), e) :> exn
                    do publishEvent(ActorEvents.ActorErrored(Local(self), err))
                    setStatus(ActorStatus.Errored(err))
                    return! shutdown()
            }
        async {
            setStatus(ActorStatus.Errored(err))
            publishEvent(ActorEvents.ActorErrored(Local(self), err))
            match config.Supervisor with
            | Null ->
                return! shutdown()  
            | ref -> 
                ref |> post <| SupervisorMessage.Errored(err)
                return! waitSupervisorResponse()  
        }
    
    let actorLoop behaviour = 
        async {
            CallContext.LogicalSetData("actor", self :> IActor)
            publishEvent(ActorEvents.ActorStarted(Local(self)))
            return! run behaviour
        }

    do Async.Start(actorLoop config.Behaviour)

    override x.ToString() = config.Path

    interface IActor with
        member x.Name with get() = config.Path
        member x.Post(msg, sender) =
               match msg with
               | :? ActorMessage as msg -> mailbox.Post(msg)
               | msg -> mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = msg}))

    interface IActor<'a> with
        member x.Name with get() = config.Path 
        member x.Post(msg:'a, sender) =
             mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = msg})) 

    interface IDisposable with  
        member x.Dispose() = ()    