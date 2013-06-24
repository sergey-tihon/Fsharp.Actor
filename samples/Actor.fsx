#load "Dependencies.fsx"
open FSharp.Actor

(**
#Basic Actors
*)

let multiplication = 
    (fun (actor:IActor<_>) ->
        let log = (actor :?> Actor.T<int * int>).Log
        let rec loop() =
            async {
                let! ((a,b), sender) = actor.Receive()
                let result = a * b
                do log.Debug(sprintf "%A: %d * %d = %d" actor.Path a b result, None)
                return! loop()
            }
        loop()
    )

let addition = 
    (fun (actor:IActor<_>) ->
        let log = (actor :?> Actor.T<int * int>).Log
        let rec loop() =
            async {
                let! ((a,b), sender) = actor.Receive()
                let result = a + b
                do log.Debug(sprintf "%A: %d + %d = %d" actor.Path a b result, None)
                return! loop()
            }
        loop()
    )

let calculator = 
    [
       Actor.spawn (Actor.Options.Create("calculator/addition")) addition
       Actor.spawn (Actor.Options.Create("calculator/multiplication")) multiplication
    ]

(**
The above code creates two actors `calcualtor/addition` and `calculator/multiplication`

    calculator/addition pre-start Status: Shutdown "Initial Startup"
    calculator/addition started Status: Running "Initial Startup"
    calculator/multiplication pre-start Status: Shutdown "Initial Startup"
    calculator/multiplication started Status: Running "Initial Startup"
    
    val multiplication : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val addition : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val calculator : FSharp.Actor.ActorRef list =
      [calculator/addition; calculator/multiplication]

We can see that the actors state transitions are logged.

Once we have created our actors we can be looked up by their path
*)
"calculator/addition" ?<-- (5,2)
"calculator/multiplication" ?<-- (5,2)

(**
Sending both of these messages yields

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

We can also send messages directly to actors if we have their `ActorRef`
*)

calculator.[0] <-- (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7

Or we could have broadcast to all of the actors in that collection
*)

calculator <-* (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

We can also resolve _systems_ of actors.
*)
"calculator" ?<-- (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

However this actor wont be found because it does not exist
*)

"calculator/addition/foo" ?<-- (5,2)

(**
resulting in a `KeyNotFoundException`

    System.Collections.Generic.KeyNotFoundException: Could not find actor calculator/addition/foo  

We can also kill actors 
*)

calculator.[1] <!- (Shutdown("Cause I want to"))

(** or *)

"calculator/addition" ?<!- (Shutdown("Cause I want to"))

(**
Sending now sending any message to the actor will result in an exception 

    System.InvalidOperationException: Actor (actor://main-pc/calculator/addition) could not handle message, State: Shutdown
*)


(**
#Changing the behaviour of actors

You can change the behaviour of actors at runtime. This achieved through mutually recursive functions
*)

let rec schizoPing = 
    (fun (actor:IActor<_>) ->
        let log = (actor :?> Actor.T<_>).Log
        let rec ping() = 
            async {
                let! (msg,_) = actor.Receive()
                log.Info(sprintf "(%A): %A ping" actor msg, None)
                return! pong()
            }
        and pong() =
            async {
                let! (msg,_) = actor.Receive()
                log.Info(sprintf "(%A): %A pong" actor msg, None)
                return! ping()
            }
        ping()
    )
        

let schizo = Actor.spawn (Actor.Options.Create("schizo")) schizoPing 

!!"schizo" <-- "Hello"

(**

Sending two messages to the 'schizo' actor results in

    (schizo): "Hello" ping

followed by

    (schizo): "Hello" pong

#Linking Actors

Linking an actor to another means that this actor will become a sibling of the other actor. This means that we can create relationships among actors
*)

let child i = 
    Actor.spawn (Actor.Options.Create(sprintf "a/child_%d" i)) 
         (fun actor ->
             let log = (actor :?> Actor.T<_>).Log 
             let rec loop() =
                async { 
                   let! msg = actor.Receive()
                   log.Info(sprintf "%A recieved %A" actor msg, None) 
                   return! loop()
                }
             loop()
         )

let parent = 
    Actor.spawnLinked (Actor.Options.Create "a/parent") (List.init 5 (child))
            (fun actor -> 
                let rec loop() =
                  async { 
                      let! msg = actor.Receive()
                      actor.Children <-* msg
                      return! loop()
                  }
                loop()    
            ) 

parent <-- "Forward this to your children"

(**
This outputs

    actor://main-pc/a/child_1 recieved "Forward this to your children"
    actor://main-pc/a/child_3 recieved "Forward this to your children"
    actor://main-pc/a/child_2 recieved "Forward this to your children"
    actor://main-pc/a/child_4 recieved "Forward this to your children"
    actor://main-pc/a/child_0 recieved "Forward this to your children"

We can also unlink actors
*)

Actor.unlink !*"a/child_0" parent

parent <-- "Forward this to your children"

(**
This outputs

    actor://main-pc/a/child_1 recieved "Forward this to your children"
    actor://main-pc/a/child_3 recieved "Forward this to your children"
    actor://main-pc/a/child_2 recieved "Forward this to your children"
    actor://main-pc/a/child_4 recieved "Forward this to your children"

#State in Actors

State in actors is managed by passing an extra parameter around the loops. For example,
*)

let incrementer =
    Actor.spawn Actor.Options.Default (fun actor -> 
        let log = (actor :?> Actor.T<int>).Log
        let rec loopWithState (currentCount:int) = 
            async {
                let! (a,_) = actor.Receive()
                log.Debug(sprintf "Incremented count by %d" a, None) 
                return! loopWithState (currentCount + a)
            }
        loopWithState 0
    )

incrementer <-- 1
incrementer <-- 2

(**
However the if the actor dies this state is lost. We need a way of rebuilding this state. Here we can use event sourcing. We can persist the events as
they pour into the actor then on restart replay those events. 
*)

type Messages = 
    | Incr of int 
    | Seed of int list

let eventSourcedIncrementer (eventStore:IEventStore) =
    Actor.spawn Actor.Options.Default (fun actor -> 
        let log = (actor :?> Actor.T<Messages>).Log
        let rec loopWithState (currentCount:int) = 
            async {
                let! (a,_) = actor.Receive()
                match a with
                | Incr a ->
                    log.Debug(sprintf "Incremented count by %d" a, None)
                    let newState = currentCount + a
                    eventStore.Store(actor.Id, a)
                    log.Debug (sprintf "Current state %d" newState, None)
                    return! loopWithState newState
                | Seed a -> 
                    return! loopWithState (a |> List.fold (+) currentCount)    
            }
        loopWithState 0
    )

let eventStore = new InMemoryEventStore() :> IEventStore

let pIncrementer = eventSourcedIncrementer eventStore
pIncrementer.OnRestarted |> Event.add (fun actor -> 
                                        let events = 
                                            eventStore.Replay(actor.Id) 
                                            |> Async.RunSynchronously 
                                            |> Seq.toList
                                        actor <-- Seed events )

pIncrementer <-- Incr 1
pIncrementer <-- Incr 2

pIncrementer.PostSystemMessage(SystemMessage.Restart("Just testing seeding"), None)

pIncrementer <-- Incr 3

(** 
Above we are passing in a event store to store the incremental changes to the actor. We then subscribe the `OnRestarted` event. This provides
us with a hook then query the event store and replay the events to build up the Message to reseed the actor.
*)
