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

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open Fracture
open HttpMachine

type ParserDelegate(?onHeaders, ?requestBody, ?requestEnded) as p =
    [<DefaultValue>] val mutable headerName : string
    [<DefaultValue>] val mutable headerValue : string
    [<DefaultValue>] val mutable request : HttpRequestMessage
    [<DefaultValue>] val mutable body : MemoryStream

    static let contentHeaders = [|
      "Allow"
      "Content-Encoding"
      "Content-Language"
      "Content-Length"
      "Content-Location"
      "Content-MD5"
      "Content-Range"
      "Content-Type"
      "Expires"
      "Last-Modified"
    |]

    static let isContentHeader name = Array.exists ((=) name) contentHeaders

    let commitHeader() = 
        match p.headerName, p.headerValue with
        | "Host" as h, v ->
            p.request.Headers.Host <- v
            // A Host header is required. This can be used to fill in the RequestUri if a fully qualified URI was not provided.
            // However, we don't want to replace the URI if a fully qualified URI was provided, as it may have used a different protocol, e.g. https.
            // Also note that we don't fail hard if a Host was not provided. This may need to change.
            if not p.request.RequestUri.IsAbsoluteUri then
                p.request.RequestUri <- Uri(Uri("http://" + v), p.request.RequestUri)
        | h, v when isContentHeader h ->
            p.request.Content.Headers.Add(h, v)
        | h, v -> p.request.Headers.Add(h, v)
        p.headerName <- null
        p.headerValue <- null

    interface IHttpParserHandler with
        member this.OnMessageBegin(parser: HttpParser) =
            this.headerName <- null
            this.headerValue <- null
            this.body <- new MemoryStream()
            this.request <- new HttpRequestMessage(Content = new StreamContent(this.body))

        member this.OnMethod(_, m) = 
            this.request.Method <- HttpMethod m

        member this.OnRequestUri(_, requestUri) = 
            this.request.RequestUri <- Uri(requestUri)

        member this.OnFragment(_, fragment) = ()

        member this.OnQueryString(_, queryString) = ()

        member this.OnHeaderName(_, name) = 
            if not (String.IsNullOrEmpty(this.headerValue)) then
                commitHeader()
            this.headerName <- name

        member this.OnHeaderValue(_, value) = 
            if String.IsNullOrEmpty(this.headerName) then
                failwith "Got a header value without name."
            this.headerValue <- value

        member this.OnHeadersEnd(parser) = 
            if not (String.IsNullOrEmpty(this.headerValue)) then
                commitHeader()
            onHeaders |> Option.iter (fun f -> f p.request)

        member this.OnBody(_, data) =
            // XXX can we defer this check to the parser?
            if data.Count > 0 then
                p.body.Write(data.Array, data.Offset, data.Count)
                requestBody |> Option.iter (fun f -> f p.request)

        member this.OnMessageEnd(_) =
            requestEnded |> Option.iter (fun f -> f p.request)
