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
namespace Frack
#nowarn "40"

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Text
open Frack
open Fracture
open Owin

[<Sealed>]
type HttpServer(app: WebApp) as this = 
    let run socket = async {
        let! env = Request.parse socket
        do! app env
    }
    let server = new TcpServer()
    let disposable =
        server.OnConnected.Subscribe(fun (_, socket) ->
            // TODO: Consider Async.StartWithContinuations
            Async.Start <| run socket
        )
        
    // TODO: Support more than one IP/domain, e.g. to support both http and https.
    member h.Start(port) = server.Listen(IPAddress.Loopback, port)

    /// Ensures the listening socket is shutdown on disposal.
    member h.Dispose() =
        disposable.Dispose()
        server.Dispose()
        GC.SuppressFinalize(this)

    interface IDisposable with
        member h.Dispose() = h.Dispose()
