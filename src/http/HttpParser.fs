namespace Fracture.Http

open System
open System.Diagnostics.Contracts
open System.IO
open System.Net.Http
open System.Text
open FSharp.Control
open FSharp.IO
open FSharpx

type private ParserState =
  | StartLine
  | Headers
  | Body

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
        let stream : MemoryStream ref = ref null

        let updateRequest (sb: byte list) state request =
            let block = sb |> List.rev |> List.toArray
            match state with
            | StartLine ->
                let line = Encoding.ASCII.GetString(block)
                HttpParser.ParseRequestLine(line, request)
                Headers
            | Headers ->
                if Array.length block = 0 then Body else
                let line = Encoding.ASCII.GetString(block)
                HttpParser.ParseHeader(line, request)
                Headers
            | Body ->
                if request.Content = null then
                    stream := new MemoryStream()
                    request.Content <- new StreamContent(!stream)
                stream.Value.Write(block, 0, block.Length)
                Body

        let rec step acc state request = async {
            let! (chunk: ArraySegment<byte>) = inbox.Receive()
            if chunk.Count > 0 then
                let rec loop sb i state =
                    if i = chunk.Count then sb, state else
                    let c = chunk.Array.[i + chunk.Offset]
                    if c = '\r'B then
                        let state' = updateRequest sb state request
                        let j = i + 1
                        if j < chunk.Count && chunk.Array.[j + chunk.Offset] = '\n'B then
                            loop [] (j + 1) state'
                        else loop [] j state'
                    elif c = '\n'B then
                        let state' = updateRequest sb state request
                        loop [] (i + 1) state'
                    else
                        loop (c :: sb) (i + 1) state
                let acc', state' = loop [] 0 state
                return! step acc' state' request
            else
                updateRequest acc state request |> ignore
                cont request
                stream := null
                return! step [] StartLine <| new HttpRequestMessage() }
        step [] StartLine <| new HttpRequestMessage() )

    member x.Post(data) = agent.Post data

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
