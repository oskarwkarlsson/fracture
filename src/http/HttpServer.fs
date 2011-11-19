module Fracture.HttpServer

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

    let svr = TcpServer.Create((fun (data, svr, sd) -> 
        let parser =
            let parserDelegate = ParserDelegate(requestBegan = (fun (a,b) -> headers(a,b,this,sd)), 
                                                requestBody = (fun data -> (body(data, svr, sd))), 
                                                requestEnded = (fun req -> (requestEnd(req, svr, sd))))
            HttpParser(parserDelegate)
        parser.Execute(new ArraySegment<_>(data)) |> ignore))

    //ensures the listening socket is shutdown on disposal.
    let cleanUp disposing = 
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                (svr :> IDisposable).Dispose()
            disposed := true
        
    member h.Start(port) = svr.Listen(IPAddress.Loopback, port)

    member h.Send(client: Socket, response: string, close) = 
        let encoded = Encoding.ASCII.GetBytes(response)
        svr.Send(client.RemoteEndPoint, encoded, close)

    interface IDisposable with
        member h.Dispose() =
            cleanUp true
            GC.SuppressFinalize(this)
