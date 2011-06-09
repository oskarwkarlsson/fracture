namespace Fracture

open System
open System.Net.Sockets
open System.Collections.Generic
open System.Collections.Concurrent

type internal BocketPool(name, maxPoolCount, perBocketBufferSize) =
    let totalsize = (maxPoolCount * perBocketBufferSize)
    let buffer = Array.zeroCreate<byte> totalsize
    let subscriptions = new ConcurrentDictionary<int, IDisposable>()
    let dispose (args: SocketAsyncEventArgs) =
        let subscription = subscriptions.[args.GetHashCode()]
        subscription.Dispose()
        args.Dispose()
    let generate() = new SocketAsyncEventArgs()
    let pool = new ObjectPool<SocketAsyncEventArgs>(maxPoolCount, generate, cleanUp = dispose)
    let disposed = ref false

    let cleanUp disposing = 
        if not !disposed then
            if disposing then
                pool.Clear()
            disposed := true

    let checkedOperation operation onFailure =
        try 
            operation()
        with
        | :? ArgumentNullException
        | :? InvalidOperationException -> onFailure()

    let raiseDisposed() = raise(ObjectDisposedException(name))

    member this.Start(callback) =
        for n in 0 .. maxPoolCount - 1 do
            let args = new SocketAsyncEventArgs()
            let subscription = args.Completed |> Observable.subscribe callback
            subscriptions.[args.GetHashCode()] <- subscription
            args.SetBuffer(buffer, n*perBocketBufferSize, perBocketBufferSize)
            this.CheckIn(args)

    member this.CheckOut() =
        if not !disposed then
            checkedOperation pool.Get raiseDisposed
        else raiseDisposed()

    member this.CheckIn(args) =
        if not !disposed then
            // ensure the the full range of the buffer is available this may have changed
            // if the bocket was previously used for a send or connect operation.
            if args.Count < perBocketBufferSize then 
                args.SetBuffer(args.Offset, perBocketBufferSize)
            // we might be trying to update the the pool when it's already been disposed. 
            checkedOperation (fun () -> pool.Put(args)) (fun () -> dispose args)
        // the pool is kicked, dispose of it ourselves.
        else dispose args
            
    member this.Count = pool.Count

    member this.Dispose() = (this :> IDisposable).Dispose()

    override this.Finalize() = cleanUp false

    interface IDisposable with
        member this.Dispose() =
            cleanUp true
            GC.SuppressFinalize(this)
