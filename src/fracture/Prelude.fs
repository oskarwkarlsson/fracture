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
