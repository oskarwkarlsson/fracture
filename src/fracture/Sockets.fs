module Fracture.Sockets

open System
open System.Net
open System.Net.Sockets
open FSharpx
open Common
open SocketExtensions
open Pipelets

type SocketListener(pipelet: Pipelet<unit,SocketDescriptor>, backlog, perOperationBufferSize, addressFamily, socketType, protocolType) =
    // Note: The per operation buffer size must be between 288 and 1024 bytes.
    // Any less results in lost data, according to our testing. Any more,
    // and the data is held until the first receive operation.
    let perOperationBufferSize = (max 288 >> min 1024) perOperationBufferSize
    let generateSocket() = new Socket(addressFamily, socketType, protocolType)
    let socketPool = new ObjectPool<_>(backlog, generateSocket, cleanUp = disposeSocket)
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

type SocketReceiver<'a>(pipelet: Pipelet<SocketDescriptor,'a>, poolSize, perOperationBufferSize) =
    let pool = new BocketPool("receive pool", max poolSize 2, perOperationBufferSize)

type SocketSender<'a>(pipelet: Pipelet<SocketDescriptor,'a>, poolSize, perOperationBufferSize) =
    let pool = new BocketPool("send pool", max poolSize 2, perOperationBufferSize)
    // TODO: SocketSender needs to know whether the connection should
    // be closed or remain open. If it is to remain open, the SocketSender
    // needs to send the connection back to a SocketReceiver.
