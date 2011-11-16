module Fracture.Common

open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open SocketExtensions

/// Creates a Socket and binds it to specified IPEndpoint, if you want a sytem assigned one Use IPEndPoint(IPAddress.Any, 0)
let inline createSocket (ipEndPoint) =
    let socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
    socket.Bind(ipEndPoint);socket

let inline closeConnection (socket:Socket) =
    try
        if socket <> null then
            socket.Shutdown(SocketShutdown.Both)
    finally socket.Close()

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
    
let inline acquireData(args: SocketAsyncEventArgs)= 
    //process received data
    let data = Array.zeroCreate<byte> args.BytesTransferred
    Buffer.BlockCopy(args.Buffer, args.Offset, data, 0, data.Length)
    data
