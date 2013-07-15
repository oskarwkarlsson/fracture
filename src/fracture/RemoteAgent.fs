namespace Fracture

open System

type RemoteAgent(port, host) =
    let server = new TcpServer(fun socket -> ())
    member this.Start(address, port) = server.Listen(address, port)
    member this.Dispose() = server.Dispose()
    interface IDisposable with
        member x.Dispose() = x.Dispose()
