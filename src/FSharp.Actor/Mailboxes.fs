namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Threading
open FSharp.Actor
open Microsoft.FSharp.Reflection

type IMailbox = 
     inherit IDisposable
     abstract Receive : int option -> Async<obj>
     abstract Post : 'a -> unit
     abstract Length : int with get

type Mailbox() =
    let mutable inbox = ConcurrentQueue<obj>()
    let awaitMsg = new AutoResetEvent(false)

    let compareType (mType:Type) tType = 
        if FSharpType.IsUnion(tType)
        then mType.DeclaringType = tType
        else mType = tType

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
                return! await (defaultArg timeout Timeout.Infinite)
            }
        member this.Post( msg) = 
            inbox.Enqueue(msg)
            awaitMsg.Set() |> ignore
        member this.Length with get() = inbox.Count
        member x.Dispose() = 
            awaitMsg.Dispose()
            inbox <- null


