[<AutoOpen>]
module Fracture.Prelude

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

let inline threadsafeDecrement (a:int ref) = Interlocked.Decrement(a) |> ignore
let inline threadsafeIncrement (a:int ref) = Interlocked.Increment(a) |> ignore
let inline (!--) (a:int ref) = threadsafeDecrement a
let inline (!++) (a:int ref) = threadsafeIncrement a

[<AutoOpen>]
module BlockingCollectionEx =
    let private raiseTimeout() = raise(TimeoutException())

    let private tryTake timeout (pool: BlockingCollection<_>) =
        let timeout = defaultArg timeout 1000
        Task.Factory.StartNew(fun () ->
            let result = ref Unchecked.defaultof<_>
            let success = pool.TryTake(result, timeout)
            if success then
                !result
            else raiseTimeout())

    type BlockingCollection<'a> with
        member this.AsyncTryTake(?timeout) = this |> tryTake timeout |> Async.AwaitTask
        member this.TryTakeAsync(?timeout) = this |> tryTake timeout
