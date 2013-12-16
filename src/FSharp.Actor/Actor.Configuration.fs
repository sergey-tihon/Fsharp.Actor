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

    type ActorConfigurationBuilder internal() = 
        member x.Zero() = { 
            Path = Guid.NewGuid().ToString(); 
            EventStream = ActorSystem.EventStream
            Supervisor = Null; 
            Behaviour = emptyBehaviour  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorConfiguration<'a>, b:ActorConfiguration<_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorConfiguration<'a>, name) = 
            {ctx with Path = name }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorConfiguration<'a>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("supervisedBy", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorConfiguration<'a>, sup) = 
            { ctx with Supervisor = sup }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorConfiguration<'a>, es) = 
            { ctx with EventStream = es }

    let actor = new ActorConfigurationBuilder()

