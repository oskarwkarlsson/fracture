module Fracture.HttpServer
#nowarn "40"

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Fracture
open Fracture.Common
open Fracture.Pipelets
open Fracture.Http
open FSharp.Control
open FSharp.IO

type HttpServer(onRequest) =
    let disposed = ref false

    let onDisconnect endPoint = 
        Console.WriteLine(sprintf "Disconnect from %s" <| endPoint.ToString())

    let rec svr = TcpServer.Create(received = onReceive, disconnected = onDisconnect)

    and parser =
        HttpParser (fun (endPoint, request) ->
            let keepAlive =
                if request.Headers.ConnectionClose.HasValue then
                    not request.Headers.ConnectionClose.Value
                else false
            onRequest(request, svr.Send endPoint keepAlive))

    and onReceive (endPoint, data) = parser.Post (endPoint, ArraySegment<_>(data))

    //ensures the listening socket is shutdown on disposal.
    member private this.Dispose(disposing) = 
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                svr.Dispose()
            disposed := true
        
    member this.Start(port) = svr.Listen(IPAddress.Loopback, port)

    interface IDisposable with
        member this.Dispose() =
            this.Dispose(true)
            GC.SuppressFinalize(this)
