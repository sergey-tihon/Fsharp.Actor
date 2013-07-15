namespace FSharp.Actor.ZeroMq

module ZeroMQ =

    open System
    open ZeroMQ
    open FsCoreSerializer
    open FSharp.Actor
    open System.Text

    let createContext() = 
        ZeroMQ.ZmqContext.Create()

    let publisher endpoint (serialiser:ISerializer) (event:IEvent<ActorMessage>) =
        async {
            try
                use ctx = createContext()
                use socket = ctx.CreateSocket(SocketType.PUB)
                socket.Bind(endpoint)
                while true do
                    let! message = event |> Async.AwaitEvent
                    let msg = ZmqMessage()
                    let header = Frame(Encoding.UTF8.GetBytes(String.Format("{0} ",[|message.Target|]))) 
                    let payload = Frame(serialiser.Serialize(message))
                    socket.SendMessage(ZmqMessage([|header; payload|])) |> ignore
            with e -> 
                printfn "Pub error: %A" e               
        }

    let subscribe endpoint (topic:string list) (serialiser:ISerializer)  onReceived  = 
        async {
            try
                use ctx = createContext()
                use socket = ctx.CreateSocket(SocketType.SUB)
                match topic with
                | [] -> socket.SubscribeAll()
                | a when a |> List.exists ((=) "*") -> socket.SubscribeAll()
                | a -> a |> List.iter (Encoding.UTF8.GetBytes >> socket.Subscribe)
                socket.Connect(endpoint)

                while true do 
                    let msg = socket.ReceiveMessage()
                    if msg.FrameCount <> 1
                    then
                        let bytes = (msg.[1].Buffer) 
                        let result = serialiser.Deserialize(bytes) :?> ActorMessage
                        onReceived(result)
            with e -> 
                printfn "Sub erro: %A" e
        }
