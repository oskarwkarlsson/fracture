module Fracture.Tcp

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open Sockets

/// Creates a Socket and binds it to specified IPEndpoint, if you want a sytem assigned one Use IPEndPoint(IPAddress.Any, 0)
let inline createTcpSocket() = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)

let inline disconnect reuseSocket (socket: Socket) =
    if socket <> null then
        try
            if socket.Connected then
                socket.Shutdown(SocketShutdown.Both)
                socket.Disconnect(reuseSocket)
        finally
            if not reuseSocket then
                socket.Close()

/// Sends data to the socket cached in the SAEA given, using the SAEA's buffer
let inline send client completed (getArgs: unit -> SocketAsyncEventArgs) bufferLength (msg: byte[]) close = 
    let rec loop offset =
        if offset < msg.Length then
            let args = getArgs()
            let amountToSend = min (msg.Length - offset) bufferLength
            args.AcceptSocket <- client
            Buffer.BlockCopy(msg, offset, args.Buffer, args.Offset, amountToSend)
            args.SetBuffer(args.Offset, amountToSend)
            if client.Connected then 
                client.SendAsyncSafe(completed, args)
                loop (offset + amountToSend)
            else Console.WriteLine(sprintf "Connection lost to%A" client.RemoteEndPoint)
    loop 0
    if close then
        let args = getArgs()
        args.AcceptSocket <- client
        client.Shutdown(SocketShutdown.Both)
        client.DisconnectAsyncSafe(completed, args)
    
let processSend sent (args: SocketAsyncEventArgs) =
    match args.SocketError with
    | SocketError.Success ->
        let sentData = acquireData args
        // notify data sent
        sent (sentData, args.UserToken :?> EndPoint)
    | SocketError.NoBufferSpaceAvailable
    | SocketError.IOPending
    | SocketError.WouldBlock ->
        failwith "Buffer overflow or send buffer timeout" //graceful termination?  
    | _ -> args.SocketError.ToString() |> printfn "socket error on send: %s"

let processDisconnect (checkIn, disconnected) (args: SocketAsyncEventArgs) =
    // NOTE: With a socket pool, the number of active connections could be calculated by the difference of the sockets in the pool from the allowed connections.
    disconnected (args.UserToken :?> EndPoint)
    // TODO: return the socket to the socket pool for reuse.
    // All calls to DisconnectAsync should have shutdown the socket.
    // Calling connectionClose here would just duplicate that effort.
    checkIn args.AcceptSocket

/// This function is called on each send, receive, and disconnect
let internal completed (checkOutArgs, checkInArgs, checkInSocket, received, sent, disconnected, sender) args =
    let processSend = processSend sent
    let processDisconnect = processDisconnect(checkInSocket, disconnected)

    let rec completed (args: SocketAsyncEventArgs) =
        try
            match args.LastOperation with
            | SocketAsyncOperation.Receive -> processReceive args
            | SocketAsyncOperation.Send -> processSend args
            | SocketAsyncOperation.Disconnect -> processDisconnect args
            | _ -> failwith "Unknown operation: %a" args.LastOperation
        finally
            args.AcceptSocket <- null
            args.UserToken <- null
            checkInArgs args
    
    and processReceive args =
        if args.SocketError = SocketError.Success && args.BytesTransferred > 0 then
            // process received data, check if data was given on connection.
            let data = acquireData args
            // trigger received
            received (data, sender, args.AcceptSocket, args.UserToken :?> EndPoint)
            // get on with the next receive
            if args.AcceptSocket.Connected then 
                let next : SocketAsyncEventArgs = checkOutArgs()
                next.AcceptSocket <- args.AcceptSocket
                next.UserToken <- args.UserToken
                args.AcceptSocket.ReceiveAsyncSafe(completed, next)
        // 0 bytes received means the client is disconnecting.
        else disconnected (args.UserToken :?> EndPoint)
    
    completed args

[<StructAttribute>]
type Client =
    val RemoteEndPoint : IPEndPoint
    /// Disconnected allows the creator of the TcpClient to handle
    /// notification, cleanup, or reuse of the Socket.
    val private Disconnected : IPEndPoint * Socket -> unit
    val private Pool: BocketPool
    val private Socket: Socket
    new(socket: Socket, pool: BocketPool) =
        { Disconnected = ignore
          Pool = pool
          RemoteEndPoint = socket.RemoteEndPoint :?> IPEndPoint
          Socket = socket }
    new(socket: Socket, pool: BocketPool, disconnected: IPEndPoint * Socket -> unit) =
        { Disconnected = disconnected
          Pool = pool
          RemoteEndPoint = socket.RemoteEndPoint :?> IPEndPoint
          Socket = socket }

    // TODO: Add static AsyncConnect() method.

    member this.AsyncReceive() =
        let socket = this.Socket
        let pool = this.Pool
        async {
            let! args = pool.AsyncCheckOut()
            args.AcceptSocket <- socket
            let! data = socket.AsyncReceive(args)
            pool.CheckIn(args)
            return data }

    member this.AsyncSend(msg: byte[]) =
        let socket = this.Socket
        let pool = this.Pool
        let rec loop offset = async {
            if offset < msg.Length then
                if socket.Connected then 
                    let! args = pool.AsyncCheckOut()
                    args.AcceptSocket <- socket
                    let amountToSend = min (msg.Length - offset) pool.BufferSizePerBocket
                    Buffer.BlockCopy(msg, offset, args.Buffer, args.Offset, amountToSend)
                    args.SetBuffer(args.Offset, amountToSend)
                    do! socket.AsyncSend(args)
                    pool.CheckIn(args)
                    return! loop (offset + amountToSend)
                else Console.WriteLine("Connection lost to {0}", socket.RemoteEndPoint) }
        loop 0

    member this.AsyncDisconnect() = this.AsyncDisconnect(false)

    /// TODO: Once socket reuse is available, make this a public member.
    member private this.AsyncDisconnect(reuseSocket) =
        let socket = this.Socket
        let remoteEndPoint = this.RemoteEndPoint
        let pool = this.Pool
        let disconnected = this.Disconnected
        async {
            // NOTE: A try ... finally may be a good idea here.
            if socket <> null && socket.Connected then
                socket.Shutdown(SocketShutdown.Both)
                let! args = pool.AsyncCheckOut()
                do! socket.AsyncDisconnect(args, reuseSocket)
                pool.CheckIn(args)
            disconnected(remoteEndPoint, socket)
            if not reuseSocket && socket <> null then socket.Close() }

type Listener(poolSize, perOperationBufferSize, acceptBacklogCount) =
    let connectionPool = new BocketPool("connection pool", max (acceptBacklogCount * 2) 2, 0)
    let receiveSendPool = new BocketPool("regular pool", max poolSize 2, perOperationBufferSize)
    let clients = new ConcurrentDictionary<IPEndPoint, Client>()
    let connections = ref 0
    let listeningSocket = createTcpSocket()

    let connected(client: Client) =
        !++connections
        clients.TryAdd(client.RemoteEndPoint, client) |> ignore

    let disconnected(endPoint, socket) =
        !--connections
        clients.TryRemove(endPoint) |> ignore

    let accept f =
        let rec loop() = async {
            let! args = connectionPool.AsyncCheckOut()
            let! acceptSocket = listeningSocket.AsyncAccept(args)
            let client = Client(acceptSocket, receiveSendPool, disconnected)
            connected client
            Async.Start (f client)
            return! loop() }
        loop()

    let disposed = ref false
    let cleanUp disposing =
        if not !disposed then
            if disposing then
                clients.Clear()
                connectionPool.Dispose()
                receiveSendPool.Dispose()
                listeningSocket.Close()
            disposed := true

    member this.Listen(ipAddress: IPAddress, port) f =
        connectionPool.Start()
        receiveSendPool.Start()
        listeningSocket.Bind(IPEndPoint(ipAddress, port))
        // TODO: Test whether starting the max allowed accept processes in parallel is better.
        Async.Start (accept f)

    member this.Dispose() =
        cleanUp true
        GC.SuppressFinalize(this)

    override this.Finalize() = cleanUp false

    interface IDisposable with
        member this.Dispose() = this.Dispose()

type RemoteAgent(handler) =
    let received _ = ()
    let tcp = new Listener(poolSize = 30000, perOperationBufferSize = 1024, acceptBacklogCount = 10000)

    member this.Start(address, port) =
        handler |> tcp.Listen(address, port)

    member this.Stop() = tcp.Dispose()
