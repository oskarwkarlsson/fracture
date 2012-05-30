namespace Fracture.Http

open System
open System.Diagnostics.Contracts
open System.IO
open System.Net
open System.Net.Http
open System.Text
open FSharp.Control
open FSharp.IO
open FSharpx

type private ParserState =
  | StartLine of byte list * Stream * HttpRequestMessage
  | Headers of byte list * Stream * HttpRequestMessage
  | Body of byte list * int64 * Stream * HttpRequestMessage
  | Done of HttpRequestMessage

type private EPSDictionary = System.Collections.Generic.Dictionary<EndPoint, ParserState>

type HttpParser(cont) =

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

    let agent = Agent.Start(fun inbox ->
        let states = new EPSDictionary()
        let rec step () = async {
            let! (endPoint, chunk: ArraySegment<byte>) = inbox.Receive()
            if chunk.Count > 0 then
                let state =
                    if not <| states.ContainsKey(endPoint) then
                        let stream = new MemoryStream()
                        StartLine([], stream, new HttpRequestMessage(Content = new StreamContent(stream)))
                    else states.[endPoint]
                let rec loop i state =
                    if i = chunk.Count then state else
                    let c = chunk.Array.[i + chunk.Offset]
                    if c = '\r'B then
                        let state' = HttpParser.UpdateRequest state
                        let j = i + 1
                        if j < chunk.Count && chunk.Array.[j + chunk.Offset] = '\n'B then
                            loop (j + 1) state'
                        else loop j state'
                    elif c = '\n'B then
                        let state' = HttpParser.UpdateRequest state
                        loop (i + 1) state'
                    else
                        let state' =
                            match state with
                            | StartLine(bs, stream, request) -> StartLine(c::bs, stream, request)
                            | Headers(bs, stream, request) -> Headers(c::bs, stream, request)
                            | Body(bs, bytesRead, stream, request) -> Body(c::bs, bytesRead, stream, request)
                            | _ -> state
                        loop (i + 1) state'
                let state' = loop 0 state
                match state' with
                | Done request ->
                    cont(endPoint, request)
                    if states.ContainsKey(endPoint) then states.Remove(endPoint) |> ignore
                | _ -> states.[endPoint] <- state'
                return! step ()
            else
                if states.ContainsKey(endPoint) then
                    match states.[endPoint] with
                    | Done _ ->
                        states.Remove(endPoint) |> ignore
                    | state ->
                        match HttpParser.UpdateRequest state with
                        | Done request ->
                            cont(endPoint, request)
                            states.Remove(endPoint) |> ignore
                        | _ -> () // the socket may have paused. We should probably add a timeout that is reset on each received packet.
                return! step () }
        step () )

    member x.Post(endPoint, data) = agent.Post (endPoint, data)

    static member private ParseRequestLine (requestLine: string, request: HttpRequestMessage) =
        let arr = requestLine.Split([|' '|], 3)
        request.Method <- HttpMethod(arr.[0])
        let uri = arr.[1] in
        request.RequestUri <- Uri(uri, if uri.StartsWith("/") then UriKind.Relative else UriKind.Absolute)
        request.Version <- Version.Parse(arr.[2].TrimStart("HTP/".ToCharArray()))

    static member private ParseHeader (header: string, request: HttpRequestMessage) =
        let name, value =
            let pair = header.Split([|':'|], 2) in
            pair.[0], pair.[1].TrimStart(' ')
        match name, value with
        | "Host" as h, v ->
            request.Headers.Host <- v
            // A Host header is required. This can be used to fill in the RequestUri if a fully qualified URI was not provided.
            // However, we don't want to replace the URI if a fully qualified URI was provided, as it may have used a different protocol, e.g. https.
            // Also note that we don't fail hard if a Host was not provided. This may need to change.
            if not request.RequestUri.IsAbsoluteUri then
                request.RequestUri <- Uri(Uri("http://" + v), request.RequestUri)
        | h, v when h |> HttpParser.IsContentHeader ->
            request.Content.Headers.Add(h, v)
        | _ -> request.Headers.Add(name, value)

    static member private IsContentHeader(name) = Array.exists ((=) name) contentHeaders

    static member private UpdateRequest state =
        match state with
        | StartLine (bs, stream, request) ->
            let block = bs |> List.rev |> List.toArray
            let line = Encoding.ASCII.GetString(block)
            HttpParser.ParseRequestLine(line, request)
            Headers ([], stream, request)
        | Headers (bs, stream, request) ->
            match bs with
            | [] ->
                if request.Method = HttpMethod.Get || request.Method = HttpMethod.Head then
                    // TODO: This isn't technically correct, but we need to fix this later.
                    Done request
                else Body ([], 0L, stream, request)
            | _ ->
                let block = bs |> List.rev |> List.toArray
                let line = Encoding.ASCII.GetString(block)
                HttpParser.ParseHeader(line, request)
                Headers ([], stream, request)
        | Body (bs, bytesRead, stream, request) ->
            HttpParser.WriteToStream (bs, stream)
            let bytesRead = bytesRead + int64 bs.Length
            if bs.Length = 0 || (request.Content.Headers.ContentLength.HasValue && bytesRead = request.Content.Headers.ContentLength.Value) then
                Done request
            else Body ([], bytesRead, stream, request)
        | s -> s

    static member private WriteToStream (bs: byte list, stream: Stream) =
        if bs.Length <= 0 then () else
        let block = bs |> List.rev |> List.toArray
        stream.Write(block, 0, block.Length)
