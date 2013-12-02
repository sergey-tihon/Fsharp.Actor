namespace FSharp.Actor

open System
open System.Threading
#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module ActorConfiguration = 
    
    let rec internal emptyBehaviour = 
        Behaviour (fun msg ->
             let rec b msg = async { return Behaviour(b) }
             b msg)

    type ActorDefinitionBuilder internal() = 
        member x.Yield(()) = { 
            Path = Guid.NewGuid().ToString(); 
            Mailbox = new DefaultMailbox<ActorMessage<'a>>(128); 
            EventStream = EventStream.Null
            ReceiveTimeout = Timeout.Infinite;
            Supervisor = Null; 
            Behaviour = emptyBehaviour  }
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorDefinition<'a>, b:ActorDefinition<_>) = { b with Mailbox = ctx.Mailbox }
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorDefinition<'a>, name) = 
            {ctx with Path = name }
        [<CustomOperation("receiveFrom", MaintainsVariableSpace = true)>]
        member x.ReceiveFrom(ctx:ActorDefinition<'a>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("timeoutAfter", MaintainsVariableSpace = true)>]
        member x.TimeoutAfter(ctx:ActorDefinition<'a>, timeout) = 
            {ctx with ReceiveTimeout = timeout }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorDefinition<'a>, behaviour) = 
            { ctx with Behaviour = Behaviour behaviour }
        [<CustomOperation("supervisedBy", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorDefinition<'a>, sup) = 
            { ctx with Supervisor = sup }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorDefinition<'a>, es) = 
            { ctx with EventStream = EventStream(es)}

    let actor = new ActorDefinitionBuilder()

module Actor = 

    let create config = 
        (new Actor<_>(config) :> IActor<_>)

    let unType<'a> (actor:IActor<'a>) = 
        (actor :?> Actor<'a>) :> IActor

    let reType<'a> (actor:IActor) = 
        (actor :?> Actor<'a>) :> IActor<'a>

    let spawn config = 
        printfn "spawning %A" config.Path
        config |> (create >> unType >> register)