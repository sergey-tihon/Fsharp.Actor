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
                
type Actor<'a>(defn:ActorDefinition<'a>) as self = 
    let mailbox = new DefaultMailbox<Message<'a>>() :> IMailbox<_>
    let logger = Logger.create defn.Path
    let systemMailbox = new DefaultMailbox<SystemMessage>() :> IMailbox<_>
    let mutable cts = new CancellationTokenSource()
    let mutable messageHandlerCancel = new CancellationTokenSource()
    let mutable defn = defn
    let mutable ctx = { Mailbox = mailbox; Logger = logger; Children = []; }
    let mutable status = ActorStatus.Stopped

    let publishEvent event = 
        defn.EventStream.Publish(event)

    let setStatus stats = 
        status <- stats

    let shutdown() = 
        async {
            messageHandlerCancel.Cancel()
            publishEvent(ActorEvents.ActorShutdown(Local(self)))
            match status with
            | Errored(err) -> logger.Debug("{0} shutdown due to Error: {1}",[|self;err|], None)
            | _ -> logger.Debug("{0} shutdown",[|self|], None)
            setStatus ActorStatus.Stopped
            return ()
        }

    let handleError (err:exn) =
        async {
            setStatus(ActorStatus.Errored(err))
            publishEvent(ActorEvents.ActorErrored(Local(self), err))
            match defn.Supervisor with
            | Null -> return! shutdown()  
            | ref -> 
                ref |> post <| SupervisorMessage.Errored(err)
                return ()  
        }

    let rec messageHandler() =
        setStatus ActorStatus.Running
        async {
            try
                do! defn.Behaviour ctx
            with e -> 
                do! handleError e
        }

    let rec restart() =
        async { 
            publishEvent(ActorEvents.ActorRestart(Local(self)))
            do messageHandlerCancel.Cancel()
            match status with
            | Errored(err) -> logger.Debug("{0} restarted due to Error: {1}",[|self;err|], None)
            | _ -> logger.Debug("{0} restarted",[|self|], None)
            do start()
            return! systemMessageHandler()
        }

    and systemMessageHandler() = 
        async {
            let! sysMsg = systemMailbox.Receive(Timeout.Infinite)
            match sysMsg with
            | Shutdown -> return! shutdown()
            | Restart -> return! restart()
            | Link(ref) -> 
                ctx <- { ctx with Children = (ref :: ctx.Children) }
                return! systemMessageHandler()
            | Unlink(ref) -> 
                ctx <- { ctx with Children = (List.filter ((<>) ref) ctx.Children) }
                return! systemMessageHandler()
            | SetSupervisor(ref) ->
               defn <- { defn with Supervisor =  ref }
               return! systemMessageHandler()
        }

    and start() = 
        if messageHandlerCancel <> null
        then
            messageHandlerCancel.Dispose()
            messageHandlerCancel <- null
        messageHandlerCancel <- new CancellationTokenSource()
        Async.Start(async {
                        CallContext.LogicalSetData("actor", self :> IActor)
                        publishEvent(ActorEvents.ActorStarted(Local(self)))
                        do! messageHandler()
                    }, messageHandlerCancel.Token)

    do 
        Async.Start(systemMessageHandler(), cts.Token)
        start()
   
    override x.ToString() = defn.Path

    interface IActor with
        member x.Name with get() = defn.Path.ToLower()
        member x.Post(msg, sender) =
               match msg with
               | :? SystemMessage as msg -> systemMailbox.Post(msg)
               | msg -> mailbox.Post({Target = Local(x); Sender = sender; Message = unbox<'a> msg})

    interface IActor<'a> with
        member x.Name with get() = defn.Path.ToLower()
        member x.Post(msg:'a, sender) =
             mailbox.Post({Target = Local(x); Sender = sender; Message = msg}) 

    interface IDisposable with  
        member x.Dispose() =
            messageHandlerCancel.Dispose()
            cts.Dispose()
           
            