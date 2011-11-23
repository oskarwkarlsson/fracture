namespace Fracture.Http

open System
open System.IO
open Fracture
open FSharp.Control
open HttpMachine
open HttpResponse

type HttpApplication = HttpRequestHeaders -> AsyncSeq<ArraySegment<byte>> -> HttpResponse

type HttpListener (poolSize, perOperationBufferSize, acceptBacklogCount) =
    let listener = new Tcp.Listener(poolSize, perOperationBufferSize, acceptBacklogCount)   

    let disposed = ref false
    let cleanUp disposing =
        if not !disposed then
            if disposing then
                listener.Dispose()
            disposed := true

    let runHttp (f: HttpApplication) (client: Tcp.Client) =
        // TODO: Remove this value and replace with the actual value from the parser. The default depends on the HTTP version specified in the request.
        let keepAlive = true

        // Create an HTTP parser.
        // TODO: Flip the parser delegate inside out using Async.FromContinuations to allow us to retrieve the values and build the headers and body iteratee.
//        let parserDelegate = ParserDelegate(onHeaders = (fun (headers, keepAlive) -> headers(headers, keepAlive, client)), 
//                                            requestBody = (fun data -> (body(data, client))), 
//                                            requestEnded = (fun req -> (requestEnd(req, client))))
//        let parser = HttpParser(parserDelegate)
    
        // Receive in a loop until the headers are read.
//        parser.Execute(new ArraySegment<_>(data)) |> ignore

        // Create an AsyncSeq of the remaining content to be read.
//        parser.Execute(new ArraySegment<_>(data)) |> ignore

        // Execute the provided function on the request and content.
        let response = HttpResponse.empty // <-- Should call f on the request headers and body iteratee.
        
        // Send the response.
        async {
            do! client.AsyncSend(response |> HttpResponse.toArray)
            if not keepAlive then
                do! client.AsyncDisconnect() }

    member this.Listen (ipAddress, port) f =
        listener.Listen(ipAddress, port) (runHttp f)

    member this.Dispose() =
        cleanUp true
        GC.SuppressFinalize(this)

    override this.Finalize() = cleanUp false

    interface IDisposable with
        member this.Dispose() = this.Dispose()
