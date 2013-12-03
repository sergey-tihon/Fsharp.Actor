#r @"..\..\packages\NLog.2.0.1.2\lib\net45\NLog.dll"
#load "Trie.fs"
#load "Types.fs"
#load "Exceptions.fs"
#load "Mailbox.fs"
#load "Logger.fs"
#load "EventStream.fs"
#load "Transport.fs"
#load "Actor.Operations.fs"
#load "Actor.Impl.fs"
#load "Actor.fs"
#load "Supervisor.fs"

open System
open FSharp.Actor

let logger = Logger.create "console"
let es = new DefaultEventStream() :> IEventStream

es.Subscribe(function
             | ActorStarted(ref) -> logger.Debug("Actor Started {0}",[|ref|], None)
             | ActorShutdown(ref) -> logger.Debug("Actor Shutdown {0}",[|ref|], None)
             | ActorRestart(ref) -> logger.Debug(sprintf "Actor Restart {0}",[|ref|], None)
             | ActorErrored(ref,err) -> logger.Error(sprintf "Actor Errored {0}", [|ref|], Some err)
             | ActorAddedChild(parent, ref) -> logger.Debug(sprintf "Linked Actors {1} -> {0}",[|parent; ref|], None)
             | ActorRemovedChild(parent, ref) -> logger.Debug(sprintf "UnLinked Actors {1} -> {0}",[|parent;ref|], None)
             )

let sup = 
    Supervisor.create (actor {
            path "error/supervisor"
        }) (fun err -> async { 
                match err.Error with
                | :? InvalidOperationException -> return SupervisorResponse.Restart
                | :? ArgumentException -> return SupervisorResponse.Continue
                | _ -> return SupervisorResponse.Stop
        })
    |> Actor.register |> Actor.ref

let errorActor = 
    actor { 
        path "exampleActor"
        raiseEventsOn es
        supervisedBy sup
        messageHandler (fun (ctx, msg) ->
                          let rec loop count (ctx:ActorContext,msg:string) = 
                              async {
                                  match msg with
                                  | "OK" -> ctx.Logger.Debug("Received {0} - {1}", [|msg; count|], None)
                                  | "Continue" -> invalidArg "foo" "foo"
                                  | "Restart" -> invalidOp "op" "op"
                                  | _ -> failwithf "Boo"
                                  return Behaviour(loop (count + 1))
                              }
                          loop 0 (ctx,msg))
    } |> Actor.create |> Actor.ref

#time
for i in 1..1000 do
    errorActor |> post <| "OK"
    errorActor |> post <| "Continue"
    errorActor |> post <| "Restart"

errorActor |> post <| "foo"

