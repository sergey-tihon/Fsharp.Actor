namespace FSharp.Actor

open System
open FSharp.Actor

type EventLogEntry = {
    Id : int64
    Timestamp : DateTimeOffset
    Event : obj
}

type InMemoryEventStore() =
    let log = ref (Map.empty<string, int64 * EventLogEntry list>)
    let id = ref 0L

    interface IEventStore with
        member x.Store(path: string, event:'a) =
            match !log |> Map.tryFind path with
            | Some(id, entries) -> 
                let id = id + 1L
                log := Map.add path (id, ({ Id = id; Timestamp = DateTimeOffset.UtcNow; Event = event } :: entries)) !log
            | None -> 
                log := Map.add path (1L, [{ Id = 1L; Timestamp = DateTimeOffset.UtcNow; Event = event }]) !log
        member x.Replay(path:string) = 
            async {
                match !log |> Map.tryFind path with
                | Some(id, entries) ->
                    return entries |> List.rev |> List.map (fun x -> x.Event) |> Seq.cast 
                | None -> 
                    return Seq.empty
            }
        member x.GetLatest(path:string) =
            match !log |> Map.tryFind path with
            | Some((id, [])) -> None
            | Some((id,h::_)) -> Some (unbox<_> h.Event)
            | None -> None
        member x.ReplayFrom(path:string, from:DateTimeOffset) = 
            async { 
                match !log |> Map.tryFind path with
                | Some(id, entries) ->
                    return entries |> List.rev |> List.choose (fun x -> if x.Timestamp >= from then Some x.Event else None) |> Seq.cast
                | None -> return Seq.empty
            }
