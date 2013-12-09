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
        member x.Yield(()) = { 
            Path = Guid.NewGuid().ToString(); 
            EventStream = EventStream.Null
            Supervisor = Null; 
            Behaviour = emptyBehaviour  }
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
            { ctx with EventStream = EventStream(es)}

    let actor = new ActorDefinitionBuilder()

module Actor = 

    let create config = 
        (new Actor<_>(config))

    let ref actor = Local actor

    let unType<'a> (actor:IActor<'a>) = 
        (actor :?> Actor<'a>) :> IActor

    let reType<'a> (actor:IActor) = 
        (actor :?> Actor<'a>) :> IActor<'a>

    let register ref = ref |> unType |> register

    let spawn config = 
        config |> (create >> register )