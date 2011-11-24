namespace Fracture.Http

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Text
open Fracture
open Fracture.Sockets
open HttpMachine

type HttpServer(headers, body, requestEnd) as this = 
    let disposed = ref false

    let svr = TcpServer.Create((fun (data, svr, conn, endPoint) -> 
        let parser =
            let parserDelegate = ParserDelegate(onHeaders = (fun (a,b) -> headers(a, b, this, conn, endPoint)), 
                                                requestBody = (fun data -> (body(data, svr, conn, endPoint))), 
                                                requestEnded = (fun req -> (requestEnd(req, svr, conn, endPoint))))
            HttpParser(parserDelegate)
        parser.Execute(new ArraySegment<_>(data)) |> ignore))

    //ensures the listening socket is shutdown on disposal.
    let cleanUp disposing =
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                (svr :> IDisposable).Dispose()
            disposed := true
        
    member h.Start(port) = svr.Listen(IPAddress.Any, port)

    member h.Send(client: Socket, response: byte[], close) =
////        Console.WriteLine(Encoding.ASCII.GetString(response))
        svr.Send(client, response, close)

    interface IDisposable with
        member h.Dispose() =
            cleanUp true
            GC.SuppressFinalize(this)
