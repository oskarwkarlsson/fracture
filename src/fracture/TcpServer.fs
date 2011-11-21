namespace Fracture

open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Collections.Generic
open System.Collections.Concurrent
open Sockets
open SocketExtensions

/// Creates a new TcpServer using the specified parameters
type TcpServer(poolSize, perOperationBufferSize, acceptBacklogCount, received, ?connected, ?disconnected, ?sent) as s =
    let sent = defaultArg sent ignore
    let connected = defaultArg connected ignore
    let disconnected = defaultArg disconnected ignore
    
    // Note: 288 bytes is the minimum size for a connection.
    let connectionPool = new BocketPool("connection pool", max (acceptBacklogCount * 2) 2, 288)
    let bocketPool = new BocketPool("regular pool", max poolSize 2, perOperationBufferSize)

    // Create and fill the socket pool.
    let socketPool = new ObjectPool<Socket>(acceptBacklogCount, Tcp.createTcpSocket)

    let clients = new ConcurrentDictionary<_,_>()
    let connections = ref 0
    let listeningSocket = Tcp.createTcpSocket()
    let disposed = ref false
       
    /// Ensures the listening socket is shutdown on disposal.
    let cleanUp disposing = 
        if not !disposed then
            if disposing then
                disconnect false listeningSocket
                connectionPool.Dispose()
                socketPool.Dispose()
                bocketPool.Dispose()
            disposed := true

    let checkInSocket socket =
        disconnect true socket
        socketPool.Put socket

    let completed = Tcp.completed(bocketPool.CheckOut, bocketPool.CheckIn, checkInSocket, received, sent, (!-- connections; disconnected), s)
    
    let rec processAccept (args: SocketAsyncEventArgs) =
        match args.SocketError with
        | SocketError.Success ->
            let acceptSocket = args.AcceptSocket
            let endPoint = acceptSocket.RemoteEndPoint
            try
                // start next accept
                let socket = socketPool.Get()
                let saea = connectionPool.CheckOut()
                saea.AcceptSocket <- socket
                listeningSocket.AcceptAsyncSafe(processAccept, saea)

                // process newly connected client
                clients.AddOrUpdate(acceptSocket.RemoteEndPoint, acceptSocket, fun _ _ -> acceptSocket) |> ignore
    
                // trigger connected
                connected acceptSocket.RemoteEndPoint
                !++ connections
    
                // start receive on accepted client
                let receiveSaea = bocketPool.CheckOut()
                receiveSaea.AcceptSocket <- acceptSocket
                receiveSaea.UserToken <- endPoint
                acceptSocket.ReceiveAsyncSafe(completed, receiveSaea)
    
                // check if data was given on connection
                if args.BytesTransferred > 0 then
                    let data = acquireData args
                    // trigger received
                    in received (data, s, acceptSocket, endPoint)
            finally
                // remove the AcceptSocket because we're reusing args
                args.AcceptSocket <- null
                connectionPool.CheckIn(args)
        
        | SocketError.OperationAborted
        | SocketError.Disconnecting when !disposed -> () // stop accepting here, we're being shutdown.
        | _ -> Debug.WriteLine (sprintf "socket error on accept: %A" args.SocketError)

    /// PoolSize=10k, Per operation buffer=1k, accept backlog=10000
    static member Create(received, ?connected, ?disconnected, ?sent) =
        new TcpServer(30000, 1024, 10000, received, ?connected = connected, ?disconnected = disconnected, ?sent = sent)

    member s.Connections = connections

    /// Starts the accepting a incoming connections.
    member s.Listen(address: IPAddress, port) =
        // initialise the bocketPool
        bocketPool.Start(completed)
        connectionPool.Start(processAccept)

        // Starts listening on the specified address and port.
        // This disables nagle
        // listeningSocket.NoDelay <- true 
        listeningSocket.Bind(IPEndPoint(address, port))
        listeningSocket.Listen(acceptBacklogCount)

        for i in 1 .. acceptBacklogCount do
            listeningSocket.AcceptAsyncSafe(processAccept, connectionPool.CheckOut())

    /// Sends the specified message to the client end point if the client is registered.
    member s.Send(clientEndPoint, msg, close) =
        let success, client = clients.TryGetValue(clientEndPoint)
        if success then
            send client completed bocketPool.CheckOut perOperationBufferSize msg close
        else failwith "Could not find client %"
        
    /// Sends the specified message to the client.
    member s.Send(client: Socket, msg, close) =
        send client completed bocketPool.CheckOut perOperationBufferSize msg close

    member s.Dispose() = (s :> IDisposable).Dispose()

    override s.Finalize() = cleanUp false
        
    interface IDisposable with 
        member s.Dispose() =
            cleanUp true
            GC.SuppressFinalize(s)
