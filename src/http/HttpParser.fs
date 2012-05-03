namespace Fracture.Http

open System
open System.Diagnostics.Contracts
open System.IO
open System.Net.Http
open System.Text
open FSharpx

type HttpParser() =

    static let contentHeaders = [|"Allow";"Content-Encoding";"Content-Language";"Content-Length";"Content-Location";"Content-MD5";"Content-Range";"Content-Type";"Expires";"Last-Modified"|]

    member x.Parse(stream: Stream) = async {
        Contract.Requires(stream <> null)
        let request = new HttpRequestMessage(Content = new StreamContent(stream))
        use reader = new AsyncStreamReader(stream, Encoding.ASCII, false, 4096)
        do! HttpParser.ParseRequestLine(reader, request)
        do! HttpParser.ParseHeaders(reader, request)
        return request
    }

    static member private ParseRequestLine (reader: AsyncStreamReader, request: HttpRequestMessage) = async {
        let! requestLine = reader.ReadLine()
        let arr = requestLine.Split([|' '|], 3)
        request.Method <- HttpMethod(arr.[0])
        let uri = arr.[1] in
        request.RequestUri <- Uri(uri, if uri.StartsWith("/") then UriKind.Relative else UriKind.Absolute)
        request.Version <- Version.Parse(arr.[2].TrimStart("HTP/".ToCharArray()))
    }

    static member private ParseHeaders (reader: AsyncStreamReader, request) =
        let rec loop () = async {
            let! line = reader.ReadLine()
            if String.IsNullOrEmpty(line) then () else
            HttpParser.ParseHeader(line, request)
            return! loop ()
        }
        loop ()

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
