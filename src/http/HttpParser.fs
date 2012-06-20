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
open FSharpx.Iteratee
open FSharpx.Iteratee.Binary

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

    let readHeaders =
        let rec lines cont = readLine >>= fun bs -> skipNewline >>= check cont bs
        and check cont bs count =
            match bs, count with
            | bs, _ when ByteString.isEmpty bs -> Done(cont [], EOF)
            | bs, 0 -> Done(cont [bs], EOF)
            | _ -> lines <| fun tail -> cont <| bs::tail
        lines id
 
    let mutable state =
        match readHeaders with
        | Continue k -> k
        | _ -> fun _ -> Error(failwith "Bad initial state")

    member x.Post(chunk: ArraySegment<_>) =
        match state (Chunk <| ByteString.ofArraySegment chunk) with
        | Continue k -> state <- k
        | Error exn -> raise exn
        | Done(lines, rest) ->
            let stream = new MemoryStream()
            let request = new HttpRequestMessage(Content = new StreamContent(stream))
            match lines with
            | startLine::headers ->
                HttpParser.ParseRequestLine(ByteString.toString startLine, request)
                for header in headers do HttpParser.ParseHeader(ByteString.toString header, request)
                if request.Method = HttpMethod.Get || request.Method = HttpMethod.Head then
                    cont request
                else ()
                // Write the remaining bytes to the stream.
                // TODO: allow the request to be sent along, while retaining a reference so that we can continue writing to the stream.
//                    HttpParser.WriteToStream (bs, stream)
//                    let bytesRead = bytesRead + int64 bs.Length
//                    if bs.Length = 0 || (request.Content.Headers.ContentLength.HasValue && bytesRead = request.Content.Headers.ContentLength.Value) then
//                        Done request
//                    else Body ([], bytesRead, stream, request)
            | _ -> () // nothing was received

    static member private ParseRequestLine (requestLine: string, request: HttpRequestMessage) =
        let arr = requestLine.Split([|' '|], 3)
        request.Method <- HttpMethod(arr.[0])
        let uri = arr.[1]
        request.RequestUri <- Uri(uri, if uri.StartsWith("/") then UriKind.Relative else UriKind.Absolute)
        request.Version <- Version.Parse(arr.[2].TrimStart("HTP/".ToCharArray()))

    static member private ParseHeader (header: string, request: HttpRequestMessage) =
        let name, value =
            let pair = header.Split([|':'|], 2)
            pair.[0], if pair.Length > 1 then pair.[1].TrimStart(' ') else ""
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

    static member private WriteToStream (bs: byte list, stream: Stream) =
        if bs.Length <= 0 then () else
        let block = bs |> List.rev |> List.toArray
        stream.Write(block, 0, block.Length)
