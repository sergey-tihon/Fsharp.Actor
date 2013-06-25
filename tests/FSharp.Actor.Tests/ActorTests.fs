namespace FSharp.Actor.Tests

open System
open NUnit.Framework
open FsUnit
open FSharp.Actor
open System.Threading

[<TestFixture; Category("Unit")>]
type ``Given an Actor``() = 
    
    let createActor name = 
        Actor.spawn (Actor.Options.Create(?id = name)) (fun (actor:IActor<_>) ->
            let rec loop() = 
                async {
                    let! (msg,_) = actor.Receive()
                    do msg(actor :> IActor)
                    return! loop()
                }
            loop()
        ) 
    
    [<Test>]
    member x.``I can send a message to it``() =
        use actor = createActor(None)
        let are = new AutoResetEvent(false) 
        let wasCalled = ref false
        actor.Post((fun (_:IActor) -> wasCalled := true; are.Set() |> ignore), None)
        are.WaitOne() |> ignore
        !wasCalled |> should equal true

    [<Test>]
    member x.``I can shutdown the actor with a message``() = 
        let actor = createActor(None)
        actor <!- Shutdown("Shutdown")
        actor.Status |> should equal (ActorStatus.Shutdown("Shutdown"))

    [<Test>]
    member x.``I can shutdown the actor with dispose``() = 
        let actor = createActor(None)
        actor.Dispose()
        actor.Status |> should equal (ActorStatus.Shutdown("Disposed"))

    [<Test>]
    member x.``I can restart the actor``() = 
        let actor = createActor(None)
        let are = new AutoResetEvent(false)
        let wasCalled = ref false
        actor.PreRestart |> Event.add (fun x -> x.Status |> should equal (ActorStatus.Restarting))
        actor.OnRestarted |> Event.add (fun x -> wasCalled := true; are.Set() |> ignore)
        actor <!- Restart("Restarted")
        actor.Status |> should equal (ActorStatus.Running)
        !wasCalled |> should be True

    [<Test>]
    member x.``Two actors are equal via there Id``() = 
        use actorA = createActor(Some "AnActor")
        use actorB = createActor(Some "AnActor")
        actorA |> should equal actorB

    [<Test>]
    member x.``I can link actors together``() = 
        use parent = createActor(Some "Parent")
        use child = createActor(Some "Child")
        parent.Link(child)
        parent.Children |> List.ofSeq |> should equal [child]

    [<Test>]
    member x.``I can unlink actors``() = 
        use parent = createActor(Some "Parent")
        use child = createActor(Some "Child")
        parent.Link(child)
        parent.UnLink(child)
        parent.Children |> List.ofSeq |> should equal []

    [<Test>]
    member x.``I should not be able to send a message to an actor that is shutdown``() = 
        let actor = createActor(None)
        actor <!- Shutdown("")
        Assert.Throws<Exception>((fun _ -> actor <-- (fun (_:IActor)-> ())), """Cannot send message actor status invalid Shutdown("")""") |> ignore
