#r @"..\..\packages\NLog.2.0.1.2\lib\net45\NLog.dll"
#load "Trie.fs"
#load "Types.fs"
#load "Mailbox.fs"
#load "Logger.fs"
#load "EventStream.fs"
#load "Transport.fs"
#load "Actor.Operations.fs"
#load "Actor.Impl.fs"
#load "Actor.fs"
#load "Supervisor.fs"

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


let baselineConfig = 
    actor { 
        path "testActor"
        raiseEventsOn es
        messageHandler (fun (ctx,msg) -> 
                          let rec loop (ctx:ActorContext,msg:string) = 
                              async {
                                  printfn "%A Recieved %A from %A" ctx.Current msg ctx.Sender
                                  !!"inherited" <-- "Thanks"
                                  return Behaviour(loop)
                              }
                          loop (ctx,msg))
    } 

let orig =  baselineConfig |> Actor.spawn

let copy = 
    actor { 
        inherits baselineConfig
        path "inherited"
        messageHandler (fun (ctx, msg) ->
                          let rec loop (ctx:ActorContext,msg:string) = 
                              async {
                                  printfn "%A Recieved %A from %A" ctx.Current msg ctx.Sender
                                  return Behaviour(loop)
                              }
                          loop (ctx,msg))
    }

copy |> Actor.spawn

resolve "testActor" |> post <| "Resolved you"
resolve "inherited" |> post <| "Resolved inherited"
