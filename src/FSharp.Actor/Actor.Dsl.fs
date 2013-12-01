namespace FSharp.Actor

open System
open System.Threading
#if INTERACTIVE
open FSharp.Actor
#endif

module DSL = 

    type ActorDefinitionBuilder() = 
        member x.Yield(()) = { 
            Path = Guid.NewGuid().ToString(); 
            Mailbox = new Mailbox<ActorMessage<'a>>(128); 
            EventStream = null
            ReceiveTimeout = Timeout.Infinite;
            Supervisor = Null; 
            Behaviour = emptyBehaviour }
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorDefinition<'a>, b:ActorDefinition<_>) = b
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
        [<CustomOperation("children", MaintainsVariableSpace = true)>]
        member x.Children(ctx:ActorDefinition<'a>, children) = 
            { ctx with Children = children }

    let actor = new ActorDefinitionBuilder()