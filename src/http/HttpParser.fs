namespace Fracture.Http

open System
open System.IO
open System.Net.Http
open FSharpx

type HttpParser() =

  static let contentHeaders = [|"Allow";"Content-Encoding";"Content-Language";"Content-Length";"Content-Location";"Content-MD5";"Content-Range";"Content-Type";"Expires";"Last-Modified"|]

  static let isContentHeader name = Array.exists ((=) name) contentHeaders

  static member private ParseRequestLine (requestLine: string, request: HttpRequestMessage) =
    let arr = requestLine.Split([|' '|], 3)
    request.Method <- HttpMethod(arr.[0])
    let uri = arr.[1] in
    request.RequestUri <- Uri(uri, if uri.StartsWith("/") then UriKind.Relative else UriKind.Absolute)
    request.Version <- Version.Parse(arr.[2].TrimStart("HTP/".ToCharArray()))

  static member private ParseHeader (header: string, request: HttpRequestMessage) =
    Console.WriteLine("Reading header: " + header)
    let name, value =
      let pair = header.Split([|':'|], 2) in
      pair.[0], pair.[1].TrimStart(' ')
    match name, value with
    | "Host" as h, v ->
        request.RequestUri <- Uri(Uri("http://" + v), request.RequestUri)
        request.Headers.Host <- v
    | h, v when h |> isContentHeader ->
        request.Content.Headers.Add(h, v)
    | _ -> request.Headers.Add(name, value)

  member x.Parse(stream: Stream) =
    if stream = null then
      raise <| ArgumentNullException("stream")

    // set the stream as the stream content for the request.
    // setting this here allows us to add headers later.
    let request = new HttpRequestMessage(Content = new StreamContent(stream))

    use reader = new StreamReader(stream)

    // read the request line
    let requestLine = reader.ReadLine()
    HttpParser.ParseRequestLine(requestLine, request)

    // read headers
    let mutable line = reader.ReadLine() 
    while not <| String.IsNullOrEmpty(line) do
      HttpParser.ParseHeader(line, request)
      line <- reader.ReadLine()

    request
