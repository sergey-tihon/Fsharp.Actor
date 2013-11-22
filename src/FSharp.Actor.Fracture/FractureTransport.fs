namespace FSharp.Actor.Fracture

open System
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent

open Fracture
open Fracture.Common

type FractureTransport(listenPort:int,?log:ILogger) = 
    let log = defaultArg log (Logger.Console ("fracture:" + (string listenPort)))
    let scheme = "fracture"
    let received = new Event<_>()
        
    let onReceived(msg:byte[], server:TcpServer, socketDescriptor:SocketDescriptor) =
        match Remoting.deserialize msg with
        | Some(rm) -> received.Trigger(rm)
        | None -> log.Warning("Unable to process remote mesage", [||], None) //TODO: Log properly

    do
        try
            let l = Fracture.TcpServer.Create(onReceived)
            l.Listen(Net.IPAddress.Any, listenPort)
            log.Debug("{0} transport listening on {1}",[|scheme;listenPort|], None)
        with e -> 
            log.Error("{0} failed to create listener on port {1}",[|scheme;listenPort|], Some e)
            reraise()

    let clients = new ConcurrentDictionary<Uri, Fracture.TcpClient>()

    let tryResolve (address:Uri) (dict:ConcurrentDictionary<_,_>) = 
        match dict.TryGetValue(address) with
        | true, v -> Some v
        | _ -> None

    let getOrAdd (ctor:Uri -> Async<_>) (address:Uri) (dict:ConcurrentDictionary<Uri,_>) =
        async {
            match dict.TryGetValue(address) with
            | true, v -> return Choice1Of2 v
            | _ -> 
                let! instance = ctor address
                match instance with
                | Choice1Of2 v -> return Choice1Of2 (dict.AddOrUpdate(address, v, (fun address _ -> v)))
                | Choice2Of2 e -> return Choice2Of2 e 
        }

    let remove (address:Uri) (dict:ConcurrentDictionary<Uri,'a>) = 
        match dict.TryRemove(address) with
        | _ -> ()

    let parseAddress (address:Uri) =
        match Net.IPAddress.TryParse(address.Host) with
        | true, ip -> ip, address.Port
        | _ -> failwithf "Invalid Address %A expected address of form {IPAddress}:{Port} eg 127.0.0.1:8080" address

    let tryCreateClient (address:Uri) =
        async {
            try
                let ip,port = parseAddress address
                let endpoint = new Net.IPEndPoint(ip,port)
                let connWaitHandle = new AutoResetEvent(false)
                let client = new Fracture.TcpClient()
                client.Connected |> Observable.add(fun x -> log.Debug("%A client connected on %A" scheme x, None); connWaitHandle.Set() |> ignore) 
                client.Disconnected |> Observable.add (fun x -> log.Debug(sprintf "%A client disconnected on %A" scheme x, None); remove address clients)
                client.Start(endpoint)
                let! connected = Async.AwaitWaitHandle(connWaitHandle, 10000)
                if not <| connected 
                then return Choice2Of2(TimeoutException() :> exn)
                else return Choice1Of2 client
            with e ->
                return Choice2Of2 e
        }

    interface ITransport with
        member val Scheme = scheme with get

        member x.CreateRemoteActor(remoteAddress) = 
            RemoteActor.spawn remoteAddress x Actor.Options.Default

        member x.Send(remoteAddress, msg, sender) =
             async {
                let! client = getOrAdd tryCreateClient remoteAddress clients
                match client with
                | Choice1Of2(client) ->
                    let rm = { Target = remoteAddress; Sender = sender |> Option.map (fun s -> s.Path); Type = msg.GetType(); Body = msg } 
                    client.Send(serialiser.Serialise rm, true)
                | Choice2Of2 e -> log.Error(sprintf "%A transport failed to create client for send %A" scheme remoteAddress, Some e)
             } |> Async.Start

        member x.SendSystemMessage(remoteAddress, msg, sender) =
             async {
                let! client = getOrAdd tryCreateClient remoteAddress clients
                match client with
                | Choice1Of2(client) ->
                    let rm = { Target = remoteAddress; Sender = sender |> Option.map (fun s -> s.Path); Type = typeof<SystemMessage>; Body = msg } 
                    client.Send(serialiser.Serialise rm, true)
                | Choice2Of2 e -> log.Error(sprintf "%A transport failed to create client for send system message %A" scheme remoteAddress, Some e)
             } |> Async.Start