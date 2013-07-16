﻿//----------------------------------------------------------------------------
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

open System
open System.Collections.Generic
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.FSharp.Core
open Fracture

type Env = Owin.Environment

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Request =
    [<CompiledName("ParseStartLine")>]
    let private parseStartLine (line: string, env: Env) =
        let arr = line.Split([|' '|], 3)
        env.[Owin.Constants.requestMethod] <- arr.[0]

        let uri = Uri(arr.[1], UriKind.RelativeOrAbsolute)

        // TODO: Fix this so that the path can be determined correctly.
        env.Add(Owin.Constants.requestPathBase, "")

        if uri.IsAbsoluteUri then
            env.[Owin.Constants.requestPath] <- uri.AbsolutePath
            env.[Owin.Constants.requestQueryString] <- uri.Query
            env.[Owin.Constants.requestScheme] <- uri.Scheme
            env.RequestHeaders.["Host"] <- [|uri.Host|]
        else
            env.[Owin.Constants.requestPath] <- uri.OriginalString

        env.[Owin.Constants.requestProtocol] <- arr.[2].Trim()

    [<CompiledName("ParseHeader")>]
    let private parseHeader (header: string, env: Env) =
        // TODO: Proper header parsing and aggregation, including linear white space.
        let pair = header.Split([|':'|], 2)
        if pair.Length > 1 then
            env.RequestHeaders.[pair.[0]] <- [| pair.[1].TrimStart(' ') |]

    [<CompiledName("ShouldKeepAlive")>]
    let shouldKeepAlive (env: Env) =
        let requestHeaders = env.RequestHeaders
        let connection =
            if requestHeaders <> null && requestHeaders.Count > 0 && requestHeaders.ContainsKey("Connection") then
                requestHeaders.["Connection"]
            else Array.empty
        match string env.[Owin.Constants.requestProtocol] with
        | "HTTP/1.1" -> Array.isEmpty connection || not (connection |> Array.exists ((=) "Close"))
        | "HTTP/1.0" -> not (Array.isEmpty connection) && connection |> Array.exists ((=) "Keep-Alive")
        | _ -> false

    [<CompiledName("Parse")>]
    let parse (readStream: TcpSocketStream) =
        async {
            let env = new Env()
            // Do the parsing manually, as the reader is likely less efficient.
            use reader = new StreamReader(readStream, encoding = System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks = false, bufferSize = 4096, leaveOpen = true)
            let! requestLine = Async.AwaitTask <| reader.ReadLineAsync()
            parseStartLine(requestLine, env)
            let parsingRequestHeaders = ref true
            while !parsingRequestHeaders do
                if reader.EndOfStream then parsingRequestHeaders := false else
                // If not at the end of the stream, read the next line.
                // TODO: Account for linear white space.
                let! line = Async.AwaitTask <| reader.ReadLineAsync()
                if line = "" then
                    parsingRequestHeaders := false
                else parseHeader(line, env)
            env.Add(Owin.Constants.requestBody, readStream :> Stream)
            return env
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Response =
    open System.Net
    open System.Text

    let BS input =
        ArraySegment<byte>(input)

    [<CompiledName("GetStatusLine")>]
    let getStatusLine = function
      | 100 -> BS"HTTP/1.1 100 Continue\r\n"B
      | 101 -> BS"HTTP/1.1 101 Switching Protocols\r\n"B
      | 102 -> BS"HTTP/1.1 102 Processing\r\n"B
      | 200 -> BS"HTTP/1.1 200 OK\r\n"B
      | 201 -> BS"HTTP/1.1 201 Created\r\n"B
      | 202 -> BS"HTTP/1.1 202 Accepted\r\n"B
      | 203 -> BS"HTTP/1.1 203 Non-Authoritative Information\r\n"B
      | 204 -> BS"HTTP/1.1 204 No Content\r\n"B
      | 205 -> BS"HTTP/1.1 205 Reset Content\r\n"B
      | 206 -> BS"HTTP/1.1 206 Partial Content\r\n"B
      | 207 -> BS"HTTP/1.1 207 Multi-Status\r\n"B
      | 208 -> BS"HTTP/1.1 208 Already Reported\r\n"B
      | 226 -> BS"HTTP/1.1 226 IM Used\r\n"B
      | 300 -> BS"HTTP/1.1 300 Multiple Choices\r\n"B
      | 301 -> BS"HTTP/1.1 301 Moved Permanently\r\n"B
      | 302 -> BS"HTTP/1.1 302 Found\r\n"B
      | 303 -> BS"HTTP/1.1 303 See Other\r\n"B
      | 304 -> BS"HTTP/1.1 304 Not Modified\r\n"B
      | 305 -> BS"HTTP/1.1 305 Use Proxy\r\n"B
      | 306 -> BS"HTTP/1.1 306 Switch Proxy\r\n"B
      | 307 -> BS"HTTP/1.1 307 Temporary Redirect\r\n"B
      | 308 -> BS"HTTP/1.1 308 Permanent Redirect\r\n"B
      | 400 -> BS"HTTP/1.1 400 Bad Request\r\n"B
      | 401 -> BS"HTTP/1.1 401 Unauthorized\r\n"B
      | 402 -> BS"HTTP/1.1 402 Payment Required\r\n"B
      | 403 -> BS"HTTP/1.1 403 Forbidden\r\n"B
      | 404 -> BS"HTTP/1.1 404 Not Found\r\n"B
      | 405 -> BS"HTTP/1.1 405 Method Not Allowed\r\n"B
      | 406 -> BS"HTTP/1.1 406 Not Acceptable\r\n"B
      | 407 -> BS"HTTP/1.1 407 Proxy Authentication Required\r\n"B
      | 408 -> BS"HTTP/1.1 408 Request Timeout\r\n"B
      | 409 -> BS"HTTP/1.1 409 Conflict\r\n"B
      | 410 -> BS"HTTP/1.1 410 Gone\r\n"B
      | 411 -> BS"HTTP/1.1 411 Length Required\r\n"B
      | 412 -> BS"HTTP/1.1 412 Precondition Failed\r\n"B
      | 413 -> BS"HTTP/1.1 413 Request Entity Too Large\r\n"B
      | 414 -> BS"HTTP/1.1 414 Request-URI Too Long\r\n"B
      | 415 -> BS"HTTP/1.1 415 Unsupported Media Type\r\n"B
      | 416 -> BS"HTTP/1.1 416 Request Range Not Satisfiable\r\n"B
      | 417 -> BS"HTTP/1.1 417 Expectation Failed\r\n"B
      | 418 -> BS"HTTP/1.1 418 I'm a teapot\r\n"B
      | 422 -> BS"HTTP/1.1 422 Unprocessable Entity\r\n"B
      | 423 -> BS"HTTP/1.1 423 Locked\r\n"B
      | 424 -> BS"HTTP/1.1 424 Failed Dependency\r\n"B
      | 425 -> BS"HTTP/1.1 425 Unordered Collection\r\n"B
      | 426 -> BS"HTTP/1.1 426 Upgrade Required\r\n"B
      | 428 -> BS"HTTP/1.1 428 Precondition Required\r\n"B
      | 429 -> BS"HTTP/1.1 429 Too Many Requests\r\n"B
      | 431 -> BS"HTTP/1.1 431 Request Header Fields Too Large\r\n"B
      | 451 -> BS"HTTP/1.1 451 Unavailable For Legal Reasons\r\n"B
      | 500 -> BS"HTTP/1.1 500 Internal Server Error\r\n"B
      | 501 -> BS"HTTP/1.1 501 Not Implemented\r\n"B
      | 502 -> BS"HTTP/1.1 502 Bad Gateway\r\n"B
      | 503 -> BS"HTTP/1.1 503 Service Unavailable\r\n"B
      | 504 -> BS"HTTP/1.1 504 Gateway Timeout\r\n"B
      | 505 -> BS"HTTP/1.1 505 HTTP Version Not Supported\r\n"B
      | 506 -> BS"HTTP/1.1 506 Variant Also Negotiates\r\n"B
      | 507 -> BS"HTTP/1.1 507 Insufficient Storage\r\n"B
      | 508 -> BS"HTTP/1.1 508 Loop Detected\r\n"B
      | 509 -> BS"HTTP/1.1 509 Bandwidth Limit Exceeded\r\n"B
      | 510 -> BS"HTTP/1.1 510 Not Extended\r\n"B
      | 511 -> BS"HTTP/1.1 511 Network Authentication Required\r\n"B
      | _ -> BS"HTTP/1.1 500 Internal Server Error\r\n"B

    [<CompiledName("Send")>]
    let send (env: Env, writeStream: TcpSocketStream) = async {
        // TODO: Aggregate the response pieces and send as a whole chunk.
        // Write the status line
        let statusLine = getStatusLine <| Convert.ToInt32(env.[Owin.Constants.responseStatusCode])
        do! writeStream.AsyncWrite(statusLine.Array, statusLine.Offset, statusLine.Count)

        // Write the headers
        for (KeyValue(header, values)) in env.ResponseHeaders do
            for value in values do
                let headerBytes = BS(Encoding.ASCII.GetBytes(sprintf "%s: %s\r\n" header value))
                do! writeStream.AsyncWrite(headerBytes.Array, headerBytes.Offset, headerBytes.Count)

        // Append the Connection header if we should close the connection.
        if not <| Request.shouldKeepAlive env && not <| env.ResponseHeaders.ContainsKey("Connection") then
            let connectionHeader = BS("Connection: close\r\n"B)
            do! writeStream.AsyncWrite(connectionHeader.Array, connectionHeader.Offset, connectionHeader.Count)

        // Append the Content-Length header if it is missing.
        // TODO: Don't do this if using chunked encoding.
        if not <| env.ResponseHeaders.ContainsKey("Content-Length") then
            let length =
                if env.ResponseBody = Unchecked.defaultof<_> then 0L
                else env.ResponseBody.Length
            let contentLengthHeader = BS(Encoding.ASCII.GetBytes(sprintf "Content-Length: %d\r\n" length))
            do! writeStream.AsyncWrite(contentLengthHeader.Array, contentLengthHeader.Offset, contentLengthHeader.Count)

        // Append the Date header if it is missing.
        if not <| env.ResponseHeaders.ContainsKey("Date") then
            let date = DateTime.UtcNow.ToString("r")
            let dateHeader = BS(Encoding.ASCII.GetBytes(sprintf "Date: %s\r\n" date))
            do! writeStream.AsyncWrite(dateHeader.Array, dateHeader.Offset, dateHeader.Count)

        // Write the body separator
        do! writeStream.AsyncWrite("\r\n"B, 0, 2)

        // Write the response body
        // TODO: Set a default timeout
        let! _ = Async.AwaitIAsyncResult <| env.ResponseBody.CopyToAsync(writeStream)
        return env.Dispose()
    }

//namespace Fracture.Http
//
//open System
//open System.Collections.Generic
//open System.Net
//open System.Text
//open Fracture
//open FSharp.Control
//open HttpMachine
//open Owin
//
//type ParserDelegate(app, send: bool -> byte[] -> unit, ?onHeaders, ?requestBody, ?requestEnded) as p =
//    [<DefaultValue>] val mutable httpMethod : string
//    [<DefaultValue>] val mutable headerName : string
//    [<DefaultValue>] val mutable headerValue : string
//    [<DefaultValue>] val mutable onBody : ReplaySubject<ArraySegment<byte>>
//    [<DefaultValue>] val mutable request : Owin.Request
//    [<DefaultValue>] val mutable finished : bool
//
//    let commitHeader() = 
//        p.request.Headers.[p.headerName] <- [|p.headerValue|]
//        p.headerName <- null
//        p.headerValue <- null
//
//    let toHttpStatusCode (i:int) = Enum.ToObject(typeof<HttpStatusCode>, i)
//
//    let responseToBytes (res: Response) = 
//        let headers =
//            String.Join(
//                "\r\n",
//                [|  yield sprintf "HTTP/1.1 %i %A" res.StatusCode <| Enum.ToObject(typeof<HttpStatusCode>, res.StatusCode)
//                    for KeyValue(header, values) in res.Headers do
//                        // TODO: Fix handling of certain headers where this approach is invalid, e.g. Set-Cookie
//                        yield sprintf "%s: %s" header <| String.Join(",", values)
//                    // Add the body separator.
//                    yield "\r\n"
//                |])
//            |> Encoding.ASCII.GetBytes
//
//        asyncSeq {
//            yield ArraySegment<_>(headers)
//            yield! res.Body
//        }
//
//    let run app req keepAlive = async {
//        let! res = app req
//        for line : ArraySegment<_> in responseToBytes res do
//            send keepAlive line.Array.[line.Offset..(line.Offset + line.Count - 1)]
//    }
//
//    interface IHttpParserHandler with
//        member this.OnMessageBegin(parser: HttpParser) =
//            this.finished <- false
//            this.httpMethod <- null
//            this.headerName <- null
//            this.headerValue <- null
//            this.onBody <- ReplaySubject<ArraySegment<byte>>(32) // TODO: Determine the correct buffer size.
//            this.request <- {
//                Environment = new Dictionary<string, obj>()
//                Headers = new Dictionary<string, string[]>()
//                Body = this.onBody |> AsyncSeq.ofObservableBuffered
//            }
//            this.request.Environment.Add(Request.Version, "1.0")
//
//        member this.OnMethod( parser, m) = 
//            this.httpMethod <- m
//            this.request.Environment.Add(Request.Method, m)
//
//        member this.OnRequestUri(_, requestUri) = 
//            let uri = Uri(requestUri, UriKind.RelativeOrAbsolute)
//            this.request.Environment.Add("fracture.RequestUri", uri)
//
//            // TODO: Fix this so that the path can be determined correctly.
//            this.request.Environment.Add(Request.PathBase, "")
//
//            if uri.IsAbsoluteUri then
//                this.request.Environment.Add(Request.Path, uri.AbsolutePath)
//                this.request.Environment.Add(Request.Scheme, uri.Scheme)
//                this.request.Headers.Add("Host", [|uri.Host|])
//            else
//                this.request.Environment.Add(Request.Path, requestUri)
//
//        member this.OnFragment(_, fragment) = 
//            this.request.Environment.Add("fracture.RequestFragment", fragment)
//
//        member this.OnQueryString(_, queryString) = 
//            this.request.Environment.Add(Request.QueryString, queryString)
//
//        member this.OnHeaderName(_, name) = 
//            if not (String.IsNullOrEmpty(this.headerValue)) then
//                commitHeader()
//            this.headerName <- name
//
//        member this.OnHeaderValue(_, value) = 
//            if String.IsNullOrEmpty(this.headerName) then
//                failwith "Got a header value without name."
//            this.headerValue <- value
//
//        member this.OnHeadersEnd(parser) = 
//            if not (String.IsNullOrEmpty(this.headerValue)) then
//                commitHeader()
//
//            this.request.Environment.Add("fracture.HttpVersion", Version(parser.MajorVersion, parser.MinorVersion))
//
//            Async.StartWithContinuations(
//                run app this.request parser.ShouldKeepAlive,
//                (fun _ -> send parser.ShouldKeepAlive Array.empty |> ignore),
//                ignore, ignore)
//
//            // NOTE: This isn't technically correct as a body with these methods is undefined, not unallowed.
//            if this.httpMethod = "GET" || this.httpMethod = "HEAD" then
//                this.onBody.OnCompleted()
//                this.finished <- true
//
//            onHeaders |> Option.iter (fun f -> f this.request)
//
//        member this.OnBody(_, data) =
//            // XXX can we defer this check to the parser?
//            if data.Count > 0 && not this.finished then
//                this.onBody.OnNext data
//                requestBody |> Option.iter (fun f -> f data)
//
//        member this.OnMessageEnd(_) =
//            if not this.finished then
//                this.onBody.OnCompleted()
//                this.finished <- true
//            requestEnded |> Option.iter (fun f -> f this.request)
