module Fracture.HttpServer
#nowarn "40"

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Fracture
open Fracture.Common
open Fracture.Pipelets
open Fracture.Http
open FSharp.IO

type HttpServer(onRequest) =
    let disposed = ref false
    let parserCache = new ConcurrentDictionary<_,_>()

    let onDisconnect endPoint = 
        Console.WriteLine(sprintf "Disconnect from %s" <| endPoint.ToString())
        parserCache.TryRemove(endPoint) 
        |> fun (removed, stream: Stream) -> 
            if removed then
                stream.Write([||], 0, 0) |> ignore

    let rec svr = TcpServer.Create(received = onReceive, disconnected = onDisconnect)

    and createParser endPoint =
        let stream = new CircularStream(4096)
        let parse stream = Async.FromContinuations(fun (cont, _, _) ->
            let parser = new HttpParser()
            let request = parser.Parse(stream)
            cont request )
        async {
            use! request = parse stream
            onRequest(request, (svr: TcpServer).Send endPoint (if request.Headers.ConnectionClose.HasValue then not request.Headers.ConnectionClose.Value else false)) }
        |> Async.Start
        stream

    and onReceive (endPoint, data) =
        let parser = parserCache.AddOrUpdate(endPoint, createParser endPoint, fun _ value -> value)
        parser.Execute( ArraySegment(data) ) |> ignore
    
    //ensures the listening socket is shutdown on disposal.
    member private this.Dispose(disposing) = 
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                (svr :> IDisposable).Dispose()
            disposed := true
        
    member this.Start(port) = svr.Listen(IPAddress.Loopback, port)

    interface IDisposable with
        member this.Dispose() =
            this.Dispose(true)
            GC.SuppressFinalize(this)
