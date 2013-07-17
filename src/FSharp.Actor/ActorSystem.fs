namespace FSharp.Actor

type ActorSystem(dispatcher : IDispatcher) = 
    
    static member Default = 
        new ActorSystem(new DisruptorBasedDispatcher(

