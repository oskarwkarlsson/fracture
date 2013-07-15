namespace Fracture

open System

type RemoteAgent(port, host) =
    let server = new TcpServer()
    let disposable = server.OnConnected.Subscribe(fun (_, socket) ->
        // TODO: Use the TcpSocketStream
        ()
    )

    member this.Start(address, port) = server.Listen(address, port)
    member this.Dispose() = server.Dispose()

    interface IDisposable with
        member x.Dispose() =
            disposable.Dispose()
            x.Dispose()
