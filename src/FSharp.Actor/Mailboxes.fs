namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Reflection


type IMailbox = 
     inherit IDisposable
     abstract Receive<'a> : int option -> Async<'a>
     abstract Post : 'a -> unit
     abstract Length : int with get

type Mailbox() =
    let mutable inbox = ConcurrentQueue<obj>()
    let awaitMsg = new AutoResetEvent(false)

    let compareType (mType:Type) tType = 
        if FSharpType.IsUnion(tType)
        then mType.DeclaringType = tType
        else mType = tType

    let rec await msgTyp timeout = async {
       match inbox.TryPeek() with
       | true, msg -> 
            if compareType (msg.GetType()) msgTyp
            then 
                match inbox.TryDequeue() with
                | true, msg -> return msg
                | false, msg -> return! await msgTyp timeout   
            else return! await msgTyp timeout   
       | false, _ -> 
          let! recd = Async.AwaitWaitHandle(awaitMsg, timeout)
          if recd
          then return! await msgTyp timeout   
          else return raise(TimeoutException("Receive timed out"))     
    }
    
    interface IMailbox with  
        member this.Receive<'a>(timeout) = 
            async { 
                let! msg = await typeof<'a> (defaultArg timeout Timeout.Infinite)
                return unbox<'a> msg
            }
        member this.Post( msg) = 
            inbox.Enqueue(msg)
            awaitMsg.Set() |> ignore
        member this.Length with get() = inbox.Count
        member x.Dispose() = 
            awaitMsg.Dispose()
            inbox <- null


