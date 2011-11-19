﻿namespace Fracture

type RemoteAgent(port, host) =
    let received _ = ()
    let server = TcpServer.Create(received) 

    member this.Start(address, port) = server.Listen(address, port)
    member this.Stop = server.Dispose()