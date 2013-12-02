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
    let logger = Logger.create config.Path
    let mutable config = config
    let mutable ctx = { Current = Local(self); Logger = logger; Children = []; LastError = None; Sender = Null }
    let mutable children = []

    let publishEvent event = 
        match config.EventStream with
        | EventStream(es) -> es.Publish(event)
        | EventStream.Null -> ()

    let rec run (Behaviour(msgHandler) as behave) = 
       async {
           let! msg = mailbox.Receive(config.ReceiveTimeout)
           match msg with
           | Shutdown -> return! shutdown()
           | Restart -> return! restart()
           | SetSupervisor(ref) ->
               config <- { config with Supervisor =  ref }
               return! run behave
           | Link(ref) -> 
               children <- (ref :: children)
           | Unlink(ref) -> 
               children <- (List.filter ((<>) ref) children)
           | Msg(msg) -> 
               let! result = msgHandler ({ ctx with Sender = msg.Sender}, msg.Message) |> Async.Catch
               match result with
               | Choice1Of2(next) ->  return! run next
               | Choice2Of2(err) -> 
                   return! handleError err
       }
    
    and restart() = 
        async { 
            publishEvent(ActorEvents.ActorRestart(Local(self)))
            if ctx.LastError.IsSome
            then logger.Debug("{0} restarted due to Error: {1}",[|self;ctx.LastError.Value.Message|], None)
            else logger.Debug("{0} restarted",[|self|], None)
            return! run config.Behaviour
        }

    and shutdown() = 
        async {
            publishEvent(ActorEvents.ActorShutdown(Local(self)))
            if ctx.LastError.IsSome
            then logger.Debug("{0} restarted due to Error: {1}",[|self;ctx.LastError.Value.Message|], None)
            else logger.Debug("{0} restarted",[|self|], None)
            return ()
        }

    and handleError (err:exn) =
        let rec waitSupervisorResponse() = 
            async {                
                    let! msg = mailbox.Receive(config.ReceiveTimeout)
                    match msg with
                    | Shutdown -> return! shutdown()
                    | Restart -> return! restart()
                    | _ -> return! waitSupervisorResponse() 
            }
        async {
            ctx <- { ctx with LastError = Some err }
            publishEvent(ActorEvents.ActorErrored(Local(self), err))
            match config.Supervisor with
            | Null ->
                return! shutdown()  
            | ref -> 
                ref |> post <| Errored(err)
                return! waitSupervisorResponse()  
        }
    
    let actorLoop behaviour = 
        async {
            CallContext.LogicalSetData("actor", self :> IActor)
            return! run behaviour
        }

    do Async.Start(actorLoop config.Behaviour)

    override x.ToString() = config.Path

    interface IActor with
        member x.Name with get() = config.Path
        member x.Post(msg, sender) =
            match msg with
            | :? ActorMessage<'a> as msg -> mailbox.Post(msg)
            | msg -> mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = unbox msg})) 

    interface IActor<'a> with
        member x.Name with get() = config.Path 
        member x.Post(msg:'a, sender) = mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = msg})) 

    interface IDisposable with  
        member x.Dispose() = ()    