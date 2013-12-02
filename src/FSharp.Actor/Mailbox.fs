namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Concurrent

#if INTERACTIVE
open FSharp.Actor
#endif

type DefaultMailbox<'a>(limit) =
    let mutable disposed = false
    let mutable inbox = ConcurrentQueue<'a>()
    let awaitMsg = new AutoResetEvent(false)

    let rec await timeout = async {
       match inbox.TryDequeue() with
       | true, msg ->
          return msg
       | false, _ ->
          let! recd = Async.AwaitWaitHandle(awaitMsg, timeout)
          if recd then return! await timeout
          else return raise(TimeoutException("Receive timed out"))
    }

    interface IMailbox<'a> with
        member this.Receive(timeout) = await timeout
        member this.Post(msg) = 
            if disposed 
            then ()
            else
                inbox.Enqueue(msg)
                awaitMsg.Set() |> ignore

        member this.Dispose() = 
            inbox <- null
            disposed <- true