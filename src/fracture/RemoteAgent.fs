namespace Fracture

type RemoteAgent(port, host) =
    let server = new TcpServer(fun socket -> ()) 
    member this.Start(address, port) = server.Listen(address, port)
    member this.Stop = server.Dispose()