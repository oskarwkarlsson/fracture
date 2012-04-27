module Fracture.HttpServer

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
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
        |> fun (removed, parser: HttpParser) -> 
            if removed then
                parser.Execute(ArraySegment<_>()) |> ignore

    let rec svr = TcpServer.Create(received = onReceive, disconnected = onDisconnect)

    and createParser endPoint = 
        let stream = new CircularStream(4096)
        let parser = new HttpParser()
        // Kick off the parser and submit the result to the waiting TcpServer.Send.
        // Return the stream or even stream.Write/stream.AsyncWrite
        HttpParser(ParserDelegate(onHeaders = (fun headers -> (Console.WriteLine(sprintf "Headers: %A" headers.Headers))), //NOTE: on ab.exe without the keepalive option only the headers callback fires
                                  requestBody = (fun body -> (Console.WriteLine(sprintf "Body: %A" body))),
                                  requestEnded = fun req -> onRequest( req, (svr:TcpServer).Send endPoint req.RequestHeaders.KeepAlive) 
                   ))

    and onReceive: Func<_,_> = 
        Func<_,_>( fun (endPoint, data) -> Task.Factory.StartNew(fun () ->
          parserCache.AddOrUpdate(endPoint, createParser endPoint, fun _ value -> value )
          |> fun parser -> parser.Execute( ArraySegment(data) ) |> ignore))
        
    member h.Start(port) = svr.Listen(IPAddress.Loopback, port)

    //ensures the listening socket is shutdown on disposal.
    member private this.Dispose(disposing) = 
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                (svr :> IDisposable).Dispose()
            disposed := true

    interface IDisposable with
        member this.Dispose() =
            this.Dispose(true)
            GC.SuppressFinalize(this)
