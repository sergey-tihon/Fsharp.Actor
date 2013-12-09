namespace FSharp.Actor.ZeroMq

open FSharp.Actor
open System.Threading
open System
open ZeroMQ
open FsCoreSerializer
open System.Text

type ZeroMqMessage = {
    Target : string
    Sender : string
    Message : obj
}
with
    static member ofMessage(publisherUri:Uri, msg:Message<obj>) = 
        let transformActorRef = function
            | Local(actor) -> (Uri(publisherUri, actor.Name).ToString())
            | Remote(transport, path) -> (Uri(publisherUri, path).ToString())
            | Null -> ""

        {
            Sender = transformActorRef msg.Sender
            Target = transformActorRef msg.Target
            Message = msg.Message
        }

    member x.ToMessage() : Message<obj> = 
        let target = 
            (Uri(x.Target).GetLeftPart(UriPartial.Authority))
        {
            Sender = (resolve x.Sender)
            Target = (resolve target)
            Message = x.Message
        }

type ZeroMqTransport(pubUri:Uri, subUri:Uri, ?logger:ILogger, ?serializer:ISerializer) = 
    let logger = defaultArg logger (Logger.create ("zeromq"))
    let serializer = defaultArg serializer (new FsCoreSerializer.BinaryFormatterSerializer() :> ISerializer)
    let cts = new CancellationTokenSource()
    let zmqContext = 
        ZeroMQ.ZmqContext.Create()

    let bind uri sockType = 
        let socket = zmqContext.CreateSocket(sockType)
        socket.Bind(uri)
        socket

    let publisher, subscriber = (bind pubUri.AbsoluteUri SocketType.PUB, bind subUri.AbsoluteUri SocketType.SUB)

    let send (toSend:Message<obj>) =
        try
             let msg = ZmqMessage()
             let payload = Frame(serializer.Serialize(toSend))
             publisher.SendMessage(ZmqMessage([|payload|])) |> ignore
        with e -> 
             logger.Error("An error occured sending message", [||], Some e)              
        
    let subscribe()  = 
        async {
            try
                use socket = subscriber
                socket.SubscribeAll()
                socket.Connect(subUri.AbsoluteUri)

                while true do 
                    let msg = socket.ReceiveMessage()
                    let bytes = (msg.[0].Buffer) 
                    let result = (serializer.Deserialize(bytes) :?> ZeroMqMessage).ToMessage()
                    result.Target |> post <| result.Message                
            with e -> 
                logger.Error("An error occured sending message", [||], Some e)  
        }
    
    do
        Async.Start(subscribe(), cts.Token)


    interface IActorTransport with
        member x.Scheme with get() = "zeromq"
        member x.Post(msg) = send msg
        member x.Dispose() = cts.Dispose()
            