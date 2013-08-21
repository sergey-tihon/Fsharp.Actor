namespace FSharp.Actor

open System.Runtime.Remoting.Messaging
open System.Threading

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorOptions = {
    Mailbox : IMailbox
    SupervisorStrategy : FaultHandler
    Parent : ActorRef option
    Timeout : int option
}
with 
    static member create(?parent, ?supervisor, ?mailbox, ?timeout) = 
        {
            Mailbox = defaultArg mailbox (new Mailbox() :> IMailbox)
            SupervisorStrategy = SupervisorStrategy.AlwaysFail
            Parent = parent
            Timeout = timeout
        }

type Actor<'a> internal(path:ActorPath, comp, options) as self = 
    inherit ActorRef(path)

    let mutable cts : CancellationTokenSource = null
    let mutable isErrored = false

    let options : ActorOptions = options
    let ctx = new ActorContext(self, ?parent = options.Parent)

    let shutdown(reason) = 
        cts.Cancel()
        cts <- null
        if ctx.LastError.IsSome
        then ctx.Log.Debug(sprintf "%A shutdown due to %A Error: %A" self reason ctx.LastError.Value.Message, None)
        else ctx.Log.Debug(sprintf "%A shutdown due to %A" self reason, None)

    let errored err = 
        async {
            ctx.LastError <- Some err
            match ctx.Parent with
            | Some(parent) -> parent <-- Errored(err, self)
            | None -> shutdown()
        }

    let rec receive (ctx:ActorContext) (comp : HandleWith<'a>) = 
        let initial = comp
        async {
            try
                printfn "Listenting"
                let! msg = options.Mailbox.Receive(options.Timeout)
                printfn "Received msg %A" msg
                match msg with
                | :? SystemMessage as sysMsg -> 
                    match sysMsg with
                    | Shutdown(reason) -> shutdown()
                    | Link(ref) -> 
                         ref <-- Parent(ctx.Ref)
                         ctx.Children.Add(ref)
                    | UnLink(ref) -> ctx.Children.Remove(ref) |> ignore
                    | Parent(ref) -> ctx.Parent <- Some(ref)
                    | Errored(err, origin) -> 
                         options.SupervisorStrategy.Handle(ctx, origin, err)
                    return! receive ctx comp
                | :? SupervisorResponse as supMsg -> 
                    match supMsg with
                    | Stop -> shutdown()
                    | Restart -> return! receive ctx initial
                    | Resume -> 
                        ctx.LastError <- None
                        return! receive ctx initial
                | :? 'a as msg -> 
                    match comp with
                    | HandleWith(cont) -> 
                        let! ncont = cont ctx msg
                        match ncont with
                        | Terminate -> shutdown()
                        | ncont -> return! receive ctx ncont
                    | TimeoutHandleWith(timeout, cont) -> 
                        let ncont = Async.RunSynchronously(cont ctx (unbox<_> msg), timeout.TotalMilliseconds |> int)
                        match ncont with
                        | Terminate -> shutdown()
                        | ncont -> return! receive ctx ncont
                    | Terminate -> shutdown()
                | _ -> 
                    return! receive ctx comp
            with e -> 
                return! errored e
        }

    let start() = 
        cts <- new CancellationTokenSource()
        Async.Start(receive ctx comp, cts.Token)

    do 
        start()

    override x.Post(msg, ?from) = 
            ctx.Sender <- from
            options.Mailbox.Post(msg)

    static member create<'a>(path, comp:HandleWith<'a>, ?options) = 
         let actor = new Actor<'a>(path, comp, defaultArg options (ActorOptions.create()))
         actor :> ActorRef