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
  | StartLine of byte list * HttpRequestMessage
  | Headers of byte list * HttpRequestMessage
  | Body of byte list * HttpRequestMessage * Stream
  with
  member x.Request =
      match x with
      | StartLine (_, request) -> request
      | Headers (_, request) -> request
      | Body (_, request, _) -> request

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
            let state =
                if not <| states.ContainsKey(endPoint) then
                    StartLine([], new HttpRequestMessage())
                else states.[endPoint]
            if chunk.Count > 0 then
                let rec loop sb i state =
                    if i = chunk.Count then sb, state else
                    let c = chunk.Array.[i + chunk.Offset]
                    if c = '\r'B then
                        let state' = HttpParser.UpdateRequest state
                        let j = i + 1
                        if j < chunk.Count && chunk.Array.[j + chunk.Offset] = '\n'B then
                            loop [] (j + 1) state'
                        else loop [] j state'
                    elif c = '\n'B then
                        let state' = HttpParser.UpdateRequest state
                        loop [] (i + 1) state'
                    else
                        loop (c :: sb) (i + 1) state
                let acc', state' = loop [] 0 state
                return! step ()
            else
                let state' = HttpParser.UpdateRequest state
                cont(endPoint, state'.Request)
                states.Remove(endPoint) |> ignore
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
        | StartLine (bs, request) ->
            let block = bs |> List.rev |> List.toArray
            let line = Encoding.ASCII.GetString(block)
            HttpParser.ParseRequestLine(line, request)
            Headers ([], request)
        | Headers (bs, request) ->
            match bs with
            | [] -> Body ([], request, null)
            | _ ->
                let block = bs |> List.rev |> List.toArray
                let line = Encoding.ASCII.GetString(block)
                HttpParser.ParseHeader(line, request)
                Headers ([], request)
        | Body (bs, request, null) ->
            let stream = new MemoryStream() :> Stream
            request.Content <- new StreamContent(stream)
            HttpParser.WriteToStream (bs, stream)
            Body ([], request, stream)
        | Body (bs, request, stream) ->
            HttpParser.WriteToStream (bs, stream)
            Body ([], request, stream)

    static member private WriteToStream (bs: byte list, stream: Stream) =
        if bs.Length <= 0 then () else
        let block = bs |> List.rev |> List.toArray
        stream.Write(block, 0, block.Length)
