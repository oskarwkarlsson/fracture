module Fracture.SocketExtensions
#nowarn "40"

open System
open System.Net
open System.Net.Sockets
open FSharpx

exception SocketIssue of SocketError
    with override this.ToString() = string this.Data0

/// Helper method to make Async calls easier.  InvokeAsyncMethod ensures the callback always
/// gets called even if an error occurs or the Async method completes synchronously.
let inline private invoke(asyncMethod, f, args: SocketAsyncEventArgs) =
    fun (cont, econt, ccont) ->
        let k (args: SocketAsyncEventArgs) =
            match args.SocketError with
            | SocketError.Success -> cont <| f args
            | e -> econt <| SocketIssue e
        let rec finish cont value =
            remover.Dispose()
            cont value
        and remover : IDisposable =
            args.Completed.Subscribe
                ({ new IObserver<_> with
                    member x.OnNext(v) = finish k v
                    member x.OnError(e) = finish econt e
                    member x.OnCompleted() =
                        let msg = "Cancelling the workflow, because the Observable awaited using AwaitObservable has completed."
                        finish ccont (new System.OperationCanceledException(msg)) })
        if not (asyncMethod args) then
            finish k args

type IDisposable with
    static member Empty =
        { new IDisposable with member x.Dispose() = () }

let inline private makeObservable asyncMethod (args: SocketAsyncEventArgs) f =
    { new IObservable<_> with
        member x.Subscribe(observer) =
            let cont = observer.OnNext >> observer.OnCompleted
            invoke (asyncMethod, f, args) (cont, observer.OnError, observer.OnError)
            // Disposal is handled for us, so return an empty, no-op disposable.
            IDisposable.Empty }

let inline private invokeAsync asyncMethod (args: SocketAsyncEventArgs) f =
    Async.FromContinuations <| invoke(asyncMethod, f, args)

type Socket with 
    member s.AcceptObservable(args) = makeObservable s.AcceptAsync args id
    member s.ReceiveObservable(args) = makeObservable s.ReceiveAsync args id
    member s.SendObservable(args) = makeObservable s.SendAsync args id
    member s.ConnectObservable(args) = makeObservable s.ConnectAsync args id
    member s.DisconnectObservable(args: SocketAsyncEventArgs, ?reuseSocket) =
        let reuseSocket = defaultArg reuseSocket false
        args.DisconnectReuseSocket <- reuseSocket
        makeObservable s.DisconnectAsync args id

    member s.AsyncAccept(args) = invokeAsync s.AcceptAsync args <| fun args -> args.AcceptSocket, BS(args.Buffer, args.Offset, args.Count)
    member s.AsyncReceive(args) = invokeAsync s.ReceiveAsync args <| fun args -> BS(args.Buffer, args.Offset, args.Count)
    member s.AsyncSend(args) = invokeAsync s.SendAsync args ignore
    member s.AsyncConnect(args) = invokeAsync s.ConnectAsync args <| fun args -> args.ConnectSocket, BS(args.Buffer, args.Offset, args.Count)
    member s.AsyncDisconnect(args: SocketAsyncEventArgs, ?reuseSocket) =
        let reuseSocket = defaultArg reuseSocket false
        args.DisconnectReuseSocket <- reuseSocket
        invokeAsync s.DisconnectAsync args ignore
