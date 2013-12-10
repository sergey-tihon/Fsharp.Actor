namespace FSharp.Actor

open System
open System.Threading
#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module ActorConfiguration = 
    
    let internal emptyBehaviour ctx = 
        let rec loop() =
             async { return! loop() }
        loop()

    type ActorDefinitionBuilder internal() = 
        member x.Zero() = { 
            Path = Guid.NewGuid().ToString(); 
            EventStream = ActorSystem.EventStream
            Supervisor = Null; 
            Behaviour = emptyBehaviour  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorDefinition<'a>, b:ActorDefinition<_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorDefinition<'a>, name) = 
            {ctx with Path = name }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorDefinition<'a>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("supervisedBy", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorDefinition<'a>, sup) = 
            { ctx with Supervisor = sup }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorDefinition<'a>, es) = 
            { ctx with EventStream = es }

    let actor = new ActorDefinitionBuilder()

