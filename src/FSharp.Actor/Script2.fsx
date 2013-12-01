#r @"..\..\packages\NLog.2.0.1.2\lib\net45\NLog.dll"
#load "Trie.fs"
#load "Types.fs"
#load "Mailbox.fs"
#load "Behaviour.fs"
#load "Logger.fs"
#load "Transport.fs"
#load "Actor.Operations.fs"
#load "Actor.Impl.fs"
#load "Actor.Dsl.fs"
#load "Actor.fs"
#load "EventStream.fs"

open FSharp.Actor
open FSharp.Actor.DSL

let baselineConfig = 
    actor { 
        path "testActor"
        supervisedBy !!"/foo/bar"
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
    } |> Actor.spawn

resolve "testActor" |> post <| "Resolved you"
resolve "inherited" |> post <| "Resolved inherited"
