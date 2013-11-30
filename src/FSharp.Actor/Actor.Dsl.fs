namespace FSharp.Actor

open System
#if INTERACTIVE
open FSharp.Actor
#endif

module DSL = 

    type ActorDefinitionBuilder() = 
        member x.Yield(()) = { Path = Guid.NewGuid().ToString(); MaxQueueSize = 128; Supervisor = Null; Behaviour = emptyBehaviour }
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorDefinition<'a>, b:ActorDefinition<_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorDefinition<'a>, name) = 
            {ctx with Path = name }
        [<CustomOperation("maxQueueSize", MaintainsVariableSpace = true)>]
        member x.ReceiveFrom(ctx:ActorDefinition<'a>, queueSize) = 
            {ctx with MaxQueueSize = queueSize }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorDefinition<'a>, behaviour) = 
            { ctx with Behaviour = Behaviour behaviour }
        [<CustomOperation("supervisedBy", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorDefinition<'a>, sup) = 
            { ctx with Supervisor = sup }

    let actor = new ActorDefinitionBuilder()