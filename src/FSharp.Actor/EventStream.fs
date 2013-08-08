namespace FSharp.Actor

type IEventStream = 
    abstract Publish : 'a -> unit
    abstract Subscribe<'a> : unit -> IEvent<'a>


