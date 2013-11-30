namespace FSharp.Actor 

open System
open System.Collections.Concurrent

type IMailbox<'a> = 
    abstract Post : 'a -> unit
    abstract Receive : unit -> Async<'a>

type Mailbox<'a>(limit) =
    let mailbox = new BlockingCollection<'a>(new ConcurrentQueue<'a>(), limit)
    interface IMailbox<'a> with
        member x.Post(msg) = 
            mailbox.Add(msg)

        member x.Receive() = 
            async { return mailbox.Take() }