module Fracture.Tcp

open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open Sockets
open SocketExtensions

/// Creates a Socket and binds it to specified IPEndpoint, if you want a sytem assigned one Use IPEndPoint(IPAddress.Any, 0)
let inline createTcpSocket() = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)

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
