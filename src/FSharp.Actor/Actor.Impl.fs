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
    let mailbox = new Mailbox<ActorMessage<'a>>(10) :> IMailbox<ActorMessage<'a>>
    let mutable config = config
    let mutable children = []

    let rec run (Behaviour(msgHandler) as behave) = 
       async {
           let! msg = mailbox.Receive()
           match msg with
           | Shutdown -> return! shutdown()
           | Restart -> return! run config.Behaviour
           | SetSupervisor(ref) ->
               config <- { config with Supervisor =  ref }
               return! run behave
           | Link(ref) -> 
               children <- (ref :: children)
           | Unlink(ref) -> 
               children <- (List.filter ((<>) ref) children)
           | Msg(msg) -> 
               let! result = msgHandler ({ Current = Local(self); Sender = msg.Sender; Children = children }, msg.Message) |> Async.Catch
               match result with
               | Choice1Of2(next) ->  return! run next
               | Choice2Of2(err) -> 
                   return! handleError err
       }
    
    and shutdown() = 
        async {
            printf "Actor(%s) shutdown" config.Path
            return ()
        }

    and handleError (err:exn) =
        let rec waitSupervisorResponse() = 
            async {                
                    let! msg = mailbox.Receive()
                    match msg with
                    | Shutdown -> return! shutdown()
                    | Restart -> return! run config.Behaviour
                    | _ -> return! waitSupervisorResponse() 
            }
        async {
            match config.Supervisor with
            | Null ->
                printfn "No supervisor couldn't handle error %A" err
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

    interface IActor<'a> with
        member x.Name with get() = config.Path
        member x.Post(msg:'a, sender) = mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = msg}))
        member x.Post(msg:obj, sender) =
            match msg with
            | :? ActorMessage<'a> as msg -> mailbox.Post(msg)
            | msg -> mailbox.Post(Msg({Target = Local(x); Sender = sender; Message = unbox msg}))        