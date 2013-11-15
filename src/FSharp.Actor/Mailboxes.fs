namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Threading


#if INTERACTIVE
open FSharp.Actor
#endif

type MailboxMessage = { 
    Payload : obj
    Sender : ActorRef option
}

type Mailbox() =
    let mutable inbox = ConcurrentQueue<MailboxMessage>()
    let awaitMsg = new AutoResetEvent(false)

    let rec await timeout = async {
       match inbox.TryDequeue() with
       | true, msg -> return msg 
       | false, _ -> 
          let! recd = Async.AwaitWaitHandle(awaitMsg, timeout)
          if recd
          then return! await timeout   
          else return raise(TimeoutException("Receive timed out"))     
    }
    
    interface IMailbox with  
        member this.Receive(timeout) = 
            async { 
                let! result = await (defaultArg timeout Timeout.Infinite)
                return result.Payload, result.Sender
            }
        member this.Post(msg, sender) = 
            inbox.Enqueue({ Payload = msg; Sender = sender })
            awaitMsg.Set() |> ignore
        member this.Length with get() = inbox.Count
        member x.Dispose() = 
            awaitMsg.Dispose()
            inbox <- null