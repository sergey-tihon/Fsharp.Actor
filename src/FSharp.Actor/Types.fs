namespace FSharp.Actor

open System
open System.Threading

[<AutoOpen>]
module Types = 
    
    type ActorNotFound(name) = 
        inherit Exception(sprintf "Unable to find actor %A" name)

    type UnableToDeliverMessageException(msg) = 
        inherit Exception(msg)

    type InvalidDispatchConfiguration(msg) =
        inherit Exception(msg)

    type InvalidActorPath(invalidPath:string) =
        inherit Exception(sprintf "%A is an invalid path should be of the form {transport}://{host}:{port}/{system}@{actor}" invalidPath)
    
    type PathDescriptor = 
        | Transport of string * string * int option
        | Any
        with
            static member ofUri(uri:Uri) = 
                if uri.IsAbsoluteUri
                then 
                    if uri.Port >= 0 
                    then Transport(uri.Scheme, uri.Host, Some uri.Port)
                    else Transport(uri.Scheme, uri.Host, None)
                else Transport("any", String.Empty, None)
            static member ofString(?str) =
                let str = defaultArg str "any://"
                if str = "any://"
                then Any
                else
                    match Uri.TryCreate(str, UriKind.RelativeOrAbsolute) with
                    | true, uri -> uri
                    | false, _ -> raise(InvalidActorPath(str))
                    |> PathDescriptor.ofUri
            override x.ToString() = 
                match x with
                | Any -> "any://"
                | Transport(transport, host, Some(port)) -> 
                    String.Format("{0}://{1}:{2}/", [|transport; host; port.ToString()|])
                | Transport(transport, host, None) ->
                    String.Format("{0}://{1}/", [|transport; host;|]) 
            member x.ToUri() = Uri(x.ToString())

    type ActorPath = {
        Actor : string
        System : string   
        Descriptor : PathDescriptor
        }
    with
        override x.ToString() =  String.Format("{0}{1}@{2}", [|x.Descriptor.ToString();x.System;x.Actor|])
        member x.Uri 
            with get() = new Uri(x.ToString())     
        static member Create(path:string) =
            let uri = Uri(path, UriKind.RelativeOrAbsolute)
            let uri = 
                if uri.IsAbsoluteUri
                then uri
                else Uri(Any.ToUri(), path)
            let system, actor = 
                match uri.PathAndQuery.Split([|'@'|], StringSplitOptions.RemoveEmptyEntries) with
                | [|system; actor|] -> system, actor
                | [|actor|] -> "*", actor
                | _ -> raise(InvalidActorPath(path))
            ActorPath.Create(actor, system, PathDescriptor.ofUri(uri))
        static member Create(actor, system, ?transport) =
            { Actor = actor; System = system; Descriptor = defaultArg transport Any; }
        static member Update(path:ActorPath, ?actor, ?system, ?transport) =
            { path with
                Actor = defaultArg actor path.Actor
                System = defaultArg system path.System
                Descriptor =  defaultArg transport path.Descriptor
            }
        static member Empty = ActorPath.Create(null,null)
        static member All = ActorPath.Create("*", "*")

    type MessageEnvelope = { 
        mutable Message : obj
        mutable Properties : Map<string, obj>
        mutable Target : ActorPath
        mutable Sender : ActorPath
    }
    with
        static member Default = { Message = null;  Properties = Map.empty; Sender = ActorPath.Empty; Target = ActorPath.Empty }
        static member Factory = new Func<_>(fun () ->  MessageEnvelope.Default)
        static member Create(message, target, ?sender, ?props) = 
            { Message = message; Properties = defaultArg props Map.empty; Sender = defaultArg sender (ActorPath.Empty); Target = target }
    
    type ActorRef(path:ActorPath, onPost : (MessageEnvelope -> unit)) =
         member val Path = path with get
         member x.Post (msg : MessageEnvelope) =  onPost(msg)
         override x.ToString() =  x.Path.ToString()
         override x.Equals(y:obj) = 
             match y with
             | :? ActorRef as y -> x.Path = y.Path
             | _ -> false
         override x.GetHashCode() = x.Path.GetHashCode()
         static member (<!-) (ref:ActorRef, msg:'a) = 
                ref.Post(MessageEnvelope.Create(msg, ref.Path))
         static member (-!>) (msg:'a,ref:ActorRef) = 
                ref.Post(MessageEnvelope.Create(msg, ref.Path))
  
    type IActorRefProvider = 
         abstract Descriptor : PathDescriptor with get
         abstract TryGet : ActorPath -> ActorRef option
         abstract GetAll : ActorPath -> seq<ActorRef>
         abstract Get : ActorPath -> ActorRef
         abstract Register : ActorRef -> unit
         abstract Remove : ActorRef -> unit

    type ITransport = 
        inherit IDisposable
        inherit IActorRefProvider
        abstract Post : MessageEnvelope -> unit
        abstract Receive : IEvent<MessageEnvelope> with get
        abstract Start : unit -> unit

    type ILogger = 
        abstract Debug : string * exn option -> unit
        abstract Info : string * exn option -> unit
        abstract Warning : string * exn option -> unit
        abstract Error : string * exn option -> unit

    type IReplyChannel<'a> =
        abstract Reply : 'a -> unit
    
    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit

