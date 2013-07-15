namespace Fracture

open System
open System.IO
open System.Net
open System.Net.Sockets
open Fracture.Common
open Fracture.SocketExtensions

type internal TcpSocket =
    struct
        val Received : IEvent<byte[] * SocketDescriptor>
        val Sent : IEvent<byte[] * SocketDescriptor>
        val Send : byte[] * int * int -> unit

        new(received, sent, send) =
            { Received = received
              Sent = sent
              Send = send }
    end

module private Impl =
    let asyncRead buffer offset count (socket: TcpSocket) =
        if count = 0 then async.Return [||] else

        // TODO: This type should retain the `args` whenever the caller's offset + count does not retrieve the bytes
        // stored in the `args`. Subsequent calls should then first read from the previous args, then retrieve the next.

        async {
            let! (buffer, sd) = Async.AwaitEvent <| socket.Received
            // TODO: Move the buffer copy here
//            if buffer <> args.Buffer || offset <> args.Offset || count <> args.Count then
//                Buffer.BlockCopy(args.Buffer, args.Offset, buffer, offset, bytesRead)
            return buffer
        }

    let asyncWrite (buffer: byte[]) offset count (bocketPool: BocketPool) perOperationBufferSize keepAlive completed socket =
        if buffer = null then
            raise <| ArgumentNullException()
        if offset < 0 || offset >= buffer.Length then
            raise <| ArgumentOutOfRangeException("offset")
        if count < 0 || count > buffer.Length - offset then
            raise <| ArgumentOutOfRangeException("count")

        if count > 0 then
            let msg =
                if offset = 0 && count = buffer.Length then
                    buffer
                else buffer.[offset..offset+count-1]
            send {Socket = socket; RemoteEndPoint = socket.RemoteEndPoint :?> IPEndPoint}
                 completed
                 bocketPool.CheckOut
                 perOperationBufferSize
                 msg
                 keepAlive
            async.Return()
        else async.Zero()
    
    let read buffer offset count (socket: Socket) =
        if count = 0 then 0 else
        socket.Receive(buffer, offset, count, SocketFlags.None)

    let write (buffer: byte[]) offset count (socket: Socket) =
        if buffer = null then
            raise <| ArgumentNullException()
        if offset < 0 || offset >= buffer.Length then
            raise <| ArgumentOutOfRangeException("offset")
        if count < 0 || count > buffer.Length - offset then
            raise <| ArgumentOutOfRangeException("count")

        if count = 0 then () else
        socket.Send(buffer, offset, count, SocketFlags.None) |> ignore

/// A read-only stream wrapping a `Socket`.
/// This stream does not close or dispose the underlying socket.
type TcpSocketStream internal (socket: TcpSocket, perOperationBufferSize, completed) as x =
    inherit System.IO.Stream()

    let keepAlive = ref true

    let mutable readTimeout = 1000
    let beginRead, endRead, _ = Async.AsBeginEnd(fun (b, o, c) -> x.AsyncRead(b, o, c))

    let mutable writeTimeout = 1000
    let beginWrite, endWrite, _ = Async.AsBeginEnd(fun (b, o, c) -> x.AsyncWrite(b, o, c))

    member x.KeepAlive
        with get() = !keepAlive
        and set(v) = keepAlive := v

    /// Reads `count` bytes from the specified `buffer` starting at the specified `offset` asynchronously.
    /// It's possible that the caller is providing a different buffer, offset, or count.
    /// The `BocketPool` is highly optimized to use a single, large buffer and limit
    /// extraneous calls to methods such as `SocketAsyncEventArgs.SetBuffer`. When possible,
    /// the caller should provide the same values as were used to create the `BocketPool`.
    member x.AsyncRead(buffer: byte[], offset, count) =
        socket |> Impl.asyncRead buffer offset count

    /// Asynchronously writes `count` bytes to the `socket` starting at the specified `offset`
    /// from the provided `buffer`.
    member x.AsyncWrite(buffer: byte[], offset, count) =
        socket |> Impl.asyncWrite buffer offset count pool perOperationBufferSize !keepAlive completed

    override x.CanRead = true
    override x.CanSeek = false
    override x.CanWrite = true
    override x.Flush() = raise <| NotSupportedException()
    override x.Length = raise <| NotSupportedException()
    override x.Position
        with get() = raise <| NotSupportedException()
        and set(v) = raise <| NotSupportedException()
    override x.Seek(offset, origin) = raise <| NotSupportedException()
    override x.SetLength(value) = raise <| NotSupportedException()

    override x.ReadTimeout
        with get() = readTimeout
        and set(v) = readTimeout <- v
    override x.Read(buffer, offset, count) =
        socket |> Impl.read buffer offset count

    override x.WriteTimeout
        with get() = writeTimeout
        and set(v) = writeTimeout <- v
    override x.Write(buffer, offset, count) =
        socket |> Impl.write buffer offset count

    // Async Stream methods
#if NET45
    override x.ReadAsync(buffer, offset, count, cancellationToken) =
        Async.StartAsTask(
            x.AsyncRead(buffer, offset, count),
            cancellationToken = cancellationToken)

    override x.WriteAsync(buffer, offset, count, cancellationToken) =
        Async.StartAsTask(
            x.AsyncWrite(buffer, offset, count),
            cancellationToken = cancellationToken
        )
        :> System.Threading.Tasks.Task
#endif

    override x.BeginRead(buffer, offset, count, callback, state) =
        beginRead((buffer, offset, count), callback, state)
    override x.EndRead(asyncResult) = endRead(asyncResult)

    override x.BeginWrite(buffer, offset, count, callback, state) =
        beginWrite((buffer, offset, count), callback, state)
    override x.EndWrite(asyncResult) = endWrite(asyncResult)


/// A read-only stream wrapping a `Socket`.
/// This stream does not close or dispose the underlying socket.
type SocketReadStream(socket: Socket, pool: BocketPool, completed) as x =
    inherit System.IO.Stream()
    do if socket = null then
        raise <| ArgumentNullException()
    do if not (socket.Blocking) then
        raise <| IOException()
    do if not (socket.Connected) then
        raise <| IOException()
    do if socket.SocketType <> SocketType.Stream then
        raise <| IOException()

    // TODO: This type should probably also accept an additional, optional buffer as the first bit of data returned from a `Read`.
    // This would allow something like an HTTP parser to restore to the `SocketReadStream` any bytes read that were not part of
    // the HTTP headers.

    let mutable readTimeout = 1000
    let beginRead, endRead, _ = Async.AsBeginEnd(fun (b, o, c) -> x.AsyncRead(b, o, c))

    /// Reads `count` bytes from the specified `buffer` starting at the specified `offset` asynchronously.
    /// It's possible that the caller is providing a different buffer, offset, or count.
    /// The `BocketPool` is highly optimized to use a single, large buffer and limit
    /// extraneous calls to methods such as `SocketAsyncEventArgs.SetBuffer`. When possible,
    /// the caller should provide the same values as were used to create the `BocketPool`.
    member x.AsyncRead(buffer: byte[], offset, count) =
        socket |> Impl.asyncRead buffer offset count pool completed

    override x.CanRead = true
    override x.CanSeek = false
    override x.CanWrite = false
    override x.Flush() = raise <| NotSupportedException()
    override x.Length = raise <| NotSupportedException()
    override x.Position
        with get() = raise <| NotSupportedException()
        and set(v) = raise <| NotSupportedException()
    override x.ReadTimeout
        with get() = readTimeout
        and set(v) = readTimeout <- v
    override x.Seek(offset, origin) = raise <| NotSupportedException()
    override x.SetLength(value) = raise <| NotSupportedException()
    override x.Read(buffer, offset, count) =
        socket |> Impl.read buffer offset count
    override x.Write(buffer, offset, count) = raise <| NotSupportedException()

    // Async Stream methods
#if NET45
    override x.ReadAsync(buffer, offset, count, cancellationToken) =
        Async.StartAsTask(
            x.AsyncRead(buffer, offset, count),
            cancellationToken = cancellationToken)
#endif
    override x.BeginRead(buffer, offset, count, callback, state) =
        beginRead((buffer, offset, count), callback, state)
    override x.EndRead(asyncResult) = endRead(asyncResult)


/// A write-only stream wrapping a `Socket`.
/// This stream does not close or dispose the underlying socket.
type SocketWriteStream(socket: Socket, pool: BocketPool, perOperationBufferSize, keepAlive, completed) as x =
    inherit System.IO.Stream()
    do if socket = null then
        raise <| ArgumentNullException()
    do if not (socket.Blocking) then
        raise <| System.IO.IOException()
    do if not (socket.Connected) then
        raise <| System.IO.IOException()
    do if socket.SocketType <> SocketType.Stream then
        raise <| System.IO.IOException()

    let keepAlive = ref true
    
    let mutable writeTimeout = 1000
    let beginWrite, endWrite, _ = Async.AsBeginEnd(fun (b, o, c) -> x.AsyncWrite(b, o, c))

    member x.KeepAlive
        with get() = !keepAlive
        and set(v) = keepAlive := v

    /// Asynchronously writes `count` bytes to the `socket` starting at the specified `offset`
    /// from the provided `buffer`.
    member x.AsyncWrite(buffer: byte[], offset, count) =
        socket |> Impl.asyncWrite buffer offset count pool perOperationBufferSize !keepAlive completed

    override x.CanRead = false
    override x.CanSeek = false
    override x.CanWrite = true
    override x.Flush() = raise <| NotSupportedException()
    override x.Length = raise <| NotSupportedException()
    override x.Position
        with get() = raise <| NotSupportedException()
        and set(v) = raise <| NotSupportedException()
    override x.Seek(offset, origin) = raise <| NotSupportedException()
    override x.WriteTimeout
        with get() = writeTimeout
        and set(v) = writeTimeout <- v
    override x.SetLength(value) = raise <| NotSupportedException()
    override x.Read(buffer, offset, count) = raise <| NotSupportedException()
    override x.Write(buffer, offset, count) =
        socket |> Impl.write buffer offset count

    // Async Stream methods
#if NET45
    override x.WriteAsync(buffer, offset, count, cancellationToken) =
        Async.StartAsTask(
            x.AsyncWrite(buffer, offset, count),
            cancellationToken = cancellationToken
        )
        :> System.Threading.Tasks.Task
#endif
    override x.BeginWrite(buffer, offset, count, callback, state) =
        beginWrite((buffer, offset, count), callback, state)
    override x.EndWrite(asyncResult) = endWrite(asyncResult)


