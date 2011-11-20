namespace Fracture

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open System.Reflection
open Sockets
open SocketExtensions

///Creates a new TcpClient using the specified parameters
type TcpClient(ipEndPoint, poolSize, size) as client =
    let listeningSocket = Tcp.createTcpSocket()
    do listeningSocket.Bind(ipEndPoint)
    let pool = new BocketPool("regular pool", poolSize, size)
    let disposed = ref false
        
    //ensures the listening socket is shutdown on disposal.
    let cleanUp disposing = 
        if not !disposed then
            if disposing then
                disconnect false listeningSocket
                pool.Dispose()
            disposed := true

    let connectedEvent = new Event<_>()
    let disconnectedEvent = new Event<_>()
    let sentEvent = new Event<_>()
    let receivedEvent = new Event<_>()

    let checkInSocket =
        // TODO: switch to reusing sockets using a pool.
        disconnect false

    let completed = Tcp.completed(pool.CheckOut, pool.CheckIn, checkInSocket, receivedEvent.Trigger, sentEvent.Trigger, disconnectedEvent.Trigger, client)
        
    let processConnect (args: SocketAsyncEventArgs) =
        if args.SocketError = SocketError.Success then
            // trigger connected
            connectedEvent.Trigger(listeningSocket.RemoteEndPoint)

            //NOTE: On the client this is not received data but the data sent during connect...
            //check if data was given on connection
            //if args.BytesTransferred > 0 then
            //    let data = acquireData args
            //    //trigger received
            //    (data, listeningSocket.RemoteEndPoint) |> receivedEvent.Trigger
                
            // start receive on connection
            let nextArgs = pool.CheckOut()
            listeningSocket.ReceiveObservable(nextArgs).Subscribe(completed) |> ignore
        else args.SocketError.ToString() |> failwith "socket error on connect: %s"

    ///Creates a new TcpClient that uses a system assigned local endpoint that has 50 receive/sent Bockets and 4096 bytes backing storage for each.
    new() = new TcpClient(IPEndPoint(IPAddress.Any, 0), 50, 4096)

    ///This event is fired when a client connects.
    [<CLIEvent>]member this.Connected = connectedEvent.Publish
    ///This event is fired when a client disconnects.
    [<CLIEvent>]member this.Disconnected = disconnectedEvent.Publish
    ///This event is fired when a message is sent to a client.
    [<CLIEvent>]member this.Sent = sentEvent.Publish
    ///This event is fired when a message is received from a client.
    [<CLIEvent>]member this.Received = receivedEvent.Publish

    /// The client's socket.
    member internal this.Socket = listeningSocket

    /// Sends the specified message to the client.
    member this.Send(msg: byte[], close: bool) =
        if listeningSocket.Connected then
            send listeningSocket completed pool.CheckOut pool.BufferSizePerBocket msg close
        else listeningSocket.RemoteEndPoint :?> IPEndPoint |> disconnectedEvent.Trigger
        
    /// Connects with a remote server at the specified IPEndPoint.
    member this.Connect(ipEndPoint) = 
        // Start the bocket pool.
        do pool.Start(completed)

        // Create an args for the connect. This args will not be reused.
        // TODO: look into why a minimum of 1 byte has to be set for a connect to be successful (288 is specified on msdn).
        let args = new SocketAsyncEventArgs()
        args.SetBuffer(Array.zeroCreate<byte> 288, 0, 288)
        args.RemoteEndPoint <- ipEndPoint
        args.Completed |> Observable.add processConnect

        listeningSocket.ConnectObservable(args).Subscribe(processConnect) |> ignore

    ///Used to close the current listening socket.
    member this.Dispose() = (this :> IDisposable).Dispose()

    override this.Finalize() = cleanUp false
        
    interface IDisposable with
        member this.Dispose() =
            cleanUp true
            GC.SuppressFinalize(this)
