namespace FSharp.Actor

[<AutoOpen>]
module ActorConfig = 

//    type ActorOptions = {
//        Mailbox : IMailbox
//        SupervisorStrategy : FaultHandler
//        Parent : ActorRef option
//        ReceiveTimeout : int option
//        EventStream : IEventStream
//    }
    
   type ConfigExpr = 
       | ListenWith of IMailbox
       | Named of string
       | HandleErrorsWith of FaultHandler
       | PublishTo of IEventStream
       | LinkedTo of seq<ConfigExpr>
       
   and Actor<'a> = | Actor of ConfigExpr list

