namespace FSharp.Actor

open System
open System.Runtime.Remoting.Messaging
open System.Threading

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorOptions = {
    Path : ActorPath
    Mailbox : IMailbox
    SupervisorStrategy : FaultHandler
}
with 
    static member create(?path, ?supervisorStrategy, ?mailbox) = 
        {
            Path = (defaultArg path (Guid.NewGuid().ToString()))
            Mailbox = defaultArg mailbox (new Mailbox() :> IMailbox)
            SupervisorStrategy = defaultArg supervisorStrategy SupervisorStrategy.AlwaysFail
        }

type Actor internal(comp, options) as self = 
    inherit ActorRef(options.Path)

    let mutable cts : CancellationTokenSource = null
    let mutable isErrored = false

    let options : ActorOptions = options
    let ctx = new ActorContext(self, options.Mailbox)

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
            | Some(parent) -> 
                ctx.Log.Error(sprintf "%A errored raising to parent %A" self parent, Some err)
                parent <-- Errored(err, self)
            | None -> shutdown("error")
        }

    let rec loop (context:ActorContext) =
        async {
            CallContext.LogicalSetData("actor", self :> ActorRef)
            CallContext.LogicalSetData("context", ctx)
            try
                do! comp context 
                do shutdown("graceful shutdown")
            with e -> 
                return! errored e
        }

    let start() = 
        cts <- new CancellationTokenSource()
        ctx.Log.Debug(sprintf "%A started" self, None)
        Async.Start(loop ctx, cts.Token)

    let restart() = 
        shutdown("restarting")
        start()

    do start()

    override x.Post(msg, ?from) = 
          match box msg with
          | :? SystemMessage as sysMsg when not(isErrored) ->
               match sysMsg with
               | Shutdown(reason) -> shutdown()
               | SetParent(ref) -> 
                    ctx.Parent <- Some ref
               | RemoveParent(ref) -> 
                    match ctx.Parent with
                    | Some(p) when p = ref -> ctx.Parent <- None
                    | _ -> ()
               | Link(ref) -> 
                    ref <-- SetParent(ctx.Ref)
                    ctx.AddChild(ref)
               | UnLink(ref) -> 
                    ref <-- RemoveParent(ctx.Ref)
                    ctx.RemoveChild(ref)
               | Errored(err, origin) -> 
                    ctx.Sender <- from
                    options.SupervisorStrategy.Handle(ctx, origin, err)
          | :? SupervisorResponse as supMsg -> 
               match supMsg with
               | Stop -> shutdown("parent said stop")
               | Restart -> restart()
               | Resume -> ctx.LastError <- None
               | Forward(originator, err) -> 
                   ctx.Parent |> Option.iter(fun p -> p.Post(Errored(err, originator), Some ctx.Ref))
          | msg when not(isErrored) -> 
            ctx.Sender <- from
            options.Mailbox.Post(msg)
          | _ -> () //FIXME: Need an undeliverable message stream....


    static member create(comp, ?path, ?parent:ActorRef, ?supervisorStrategy, ?mailbox, ?children:seq<ActorRef>) = 
         let actor = new Actor(comp, (ActorOptions.create(
                                          ?path = path,
                                          ?supervisorStrategy = supervisorStrategy,
                                          ?mailbox = mailbox
                                        )
                                     )
                              ) :> ActorRef
        
         parent |> Option.iter (fun p -> actor <-- SetParent(p))
         children |> Option.iter (Seq.iter (fun child -> actor <-- Link(child)))
         actor  