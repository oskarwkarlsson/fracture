module Fracture.Sockets

open System
open System.Net
open System.Net.Sockets
open FSharpx
open SocketExtensions
open Pipelets

let inline acquireData(args: SocketAsyncEventArgs)= 
    //process received data
    let data = Array.zeroCreate<byte> args.BytesTransferred
    Buffer.BlockCopy(args.Buffer, args.Offset, data, 0, data.Length)
    data

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
let inline send client (completed: SocketAsyncEventArgs -> unit) (getArgs: unit -> SocketAsyncEventArgs) bufferLength (msg: byte[]) close = 
    let rec loop offset =
        if offset < msg.Length then
            let args = getArgs()
            let amountToSend = min (msg.Length - offset) bufferLength
            args.AcceptSocket <- client
            Buffer.BlockCopy(msg, offset, args.Buffer, args.Offset, amountToSend)
            args.SetBuffer(args.Offset, amountToSend)
            if client.Connected then 
                client.SendObservable(args).Subscribe(completed) |> ignore
                loop (offset + amountToSend)
            else Console.WriteLine(sprintf "Connection lost to%A" client.RemoteEndPoint)
    loop 0
    if close then
        let args = getArgs()
        args.AcceptSocket <- client
        client.Shutdown(SocketShutdown.Both)
        client.DisconnectObservable(args).Subscribe(completed) |> ignore
    
type SocketListener(pipelet: Pipelet<unit,Socket>, backlog, perOperationBufferSize, addressFamily, socketType, protocolType) =
    // Note: The per operation buffer size must be between 288 and 1024 bytes.
    // Any less results in lost data, according to our testing. Any more,
    // and the data is held until the first receive operation.
    let perOperationBufferSize = (max 288 >> min 1024) perOperationBufferSize
    let generateSocket() = new Socket(addressFamily, socketType, protocolType)
    let socketPool = new ObjectPool<_>(backlog, generateSocket, cleanUp = disconnect false)
    let bocketPool = new BocketPool("connection pool", max (backlog * 2) 2, perOperationBufferSize)

    // NOTE: No longer need to track clients, as the SocketListener
    // should pass a given socket off to the SocketReceiver, and if
    // the connection should remain open, the SocketSender will send
    // the connection back to the SocketReceiver and disconnect otherwise.

    // TODO: When accepting a connection, we need to also allocate and
    // assign a socket from the socketPool to the bocket.
    // Should sockets be pre-assigned to bockets?

// TODO: Are sender and receiver really any different?
// Aren't they the same thing doing a slightly different operation?
// An alternative implementation may be to pass in the operation.

// NOTE: The current design below uses separate pools for send and receive.
// TODO: Shared bocket pool? If so, who owns it? Does sharing a single pool
// give any advantage?

type SocketReceiver<'a>(pipelet: Pipelet<Socket,'a>, poolSize, perOperationBufferSize) =
    let pool = new BocketPool("receive pool", max poolSize 2, perOperationBufferSize)

type SocketSender<'a>(pipelet: Pipelet<Socket,'a>, poolSize, perOperationBufferSize) =
    let pool = new BocketPool("send pool", max poolSize 2, perOperationBufferSize)
    // TODO: SocketSender needs to know whether the connection should
    // be closed or remain open. If it is to remain open, the SocketSender
    // needs to send the connection back to a SocketReceiver.
