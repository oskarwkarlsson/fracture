//----------------------------------------------------------------------------
//
// Copyright (c) 2011-2012 Dave Thomas (@7sharp9) 
//                         Ryan Riley (@panesofglass)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//----------------------------------------------------------------------------
namespace Fracture.Http
#nowarn "40"

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Text
open System.Threading.Tasks
open Fracture
open Fracture.Common
open Fracture.Pipelets
open HttpMachine
open Owin

type HttpServer(app: Request -> Async<Response>) as this = 
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
        HttpParser(ParserDelegate(requestEnded = fun req -> onRequest( req, (svr:TcpServer).Send endPoint req.RequestHeaders.KeepAlive)))

    and onReceive (endPoint, data) =
        let parser = parserCache.AddOrUpdate(endPoint, createParser endPoint, fun _ value -> value)
        parser.Execute( ArraySegment(data) ) |> ignore
    
    //ensures the listening socket is shutdown on disposal.
    let cleanUp disposing = 
        if not !disposed then
            if disposing && svr <> Unchecked.defaultof<TcpServer> then
                (svr :> IDisposable).Dispose()
            disposed := true
        
    member this.Start(port) = svr.Listen(IPAddress.Loopback, port)

    interface IDisposable with
        member h.Dispose() =
            cleanUp true
            GC.SuppressFinalize(this)
