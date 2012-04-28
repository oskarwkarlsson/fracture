namespace Fracture.Http

open System
open System.Diagnostics.Contracts
open System.IO
open System.Net.Http
open FSharpx

type HttpParser() =

  static let contentHeaders = [|"Allow";"Content-Encoding";"Content-Language";"Content-Length";"Content-Location";"Content-MD5";"Content-Range";"Content-Type";"Expires";"Last-Modified"|]

  member x.Parse(stream: Stream) =
    Contract.Requires(stream <> null)
    let request = new HttpRequestMessage(Content = new StreamContent(stream))
    use reader = new StreamReader(stream)
    HttpParser.ParseRequestLine(reader, request)
    HttpParser.ParseHeaders(reader, request)
    request

  static member private ParseRequestLine (reader: TextReader, request: HttpRequestMessage) =
    let requestLine = reader.ReadLine()
    let arr = requestLine.Split([|' '|], 3)
    request.Method <- HttpMethod(arr.[0])
    let uri = arr.[1] in
    request.RequestUri <- Uri(uri, if uri.StartsWith("/") then UriKind.Relative else UriKind.Absolute)
    request.Version <- Version.Parse(arr.[2].TrimStart("HTP/".ToCharArray()))

  static member private ParseHeaders (reader: TextReader, request) =
    let mutable line = reader.ReadLine() 
    while not <| String.IsNullOrEmpty(line) do
      HttpParser.ParseHeader(line, request)
      line <- reader.ReadLine()

  static member private ParseHeader (header: string, request: HttpRequestMessage) =
    let name, value =
      let pair = header.Split([|':'|], 2) in
      pair.[0], pair.[1].TrimStart(' ')
    match name, value with
    | "Host" as h, v ->
        request.RequestUri <- Uri(Uri("http://" + v), request.RequestUri)
        request.Headers.Host <- v
    | h, v when h |> HttpParser.IsContentHeader ->
        request.Content.Headers.Add(h, v)
    | _ -> request.Headers.Add(name, value)

  static member private IsContentHeader(name) = Array.exists ((=) name) contentHeaders
