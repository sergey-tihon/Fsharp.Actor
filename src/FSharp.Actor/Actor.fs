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
}
with 
    static member create(?parent, ?supervisor, ?mailbox) = 
        {
            Mailbox = defaultArg mailbox (new Mailbox() :> IMailbox)
            SupervisorStrategy = SupervisorStrategy.Forward
            Parent = parent
        }

type Actor internal(path:ActorPath, comp, options) as self = 
    inherit ActorRef(path)

    let mutable cts : CancellationTokenSource = null
    let mutable isErrored = false

    let options : ActorOptions = options
    let ctx = new ActorContext(self, options.Mailbox, ?parent = options.Parent)

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

    let rec loop context =
        async {
            CallContext.LogicalSetData("actor", self :> ActorRef)
            try
                do! comp context 
            with e -> 
                return! errored e
        }

    let start() = 
        cts <- new CancellationTokenSource()
        Async.Start(loop ctx, cts.Token)

    let restart() = 
        shutdown()
        start()

    do 
        start()

    override x.Post(msg, ?from) = 
          match box msg with
          | :? SystemMessage as sysMsg when not(isErrored) ->
               match sysMsg with
               | Shutdown(reason) -> shutdown()
               | Link(ref) -> 
                    ref <-- Parent(ctx.Ref)
                    ctx.Children.Add(ref)
               | UnLink(ref) -> ctx.Children.Remove(ref) |> ignore
               | Parent(ref) -> ctx.Parent <- Some(ref)
               | Errored(err, origin) -> 
                    ctx.Sender <- from
                    options.SupervisorStrategy.Handle(ctx, origin, err)
          | :? SupervisorResponse as supMsg -> 
               match supMsg with
               | Stop -> shutdown()
               | Restart -> restart()
               | Resume -> ctx.LastError <- None
               | Forward(originator, err) -> 
                   ctx.Parent |> Option.iter(fun p -> p.Post(Errored(err, originator), Some ctx.Ref))
          | msg when not(isErrored) -> 
            ctx.Sender <- from
            options.Mailbox.Post(msg)
          | _ -> () //FIXME: Need an undeliverable message stream....

    static member create(path, comp, ?options) = 
         let actor = new Actor(path, comp, defaultArg options (ActorOptions.create()))
         actor :> ActorRef