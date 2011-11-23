namespace Fracture.Http

open System
open Fracture
open FSharp.Control
open HttpMachine

type HttpListener (poolSize, perOperationBufferSize, acceptBacklogCount) =
    let listener = new Tcp.Listener(poolSize, perOperationBufferSize, acceptBacklogCount)   

    let disposed = ref false
    let cleanUp disposing =
        if not !disposed then
            if disposing then
                listener.Dispose()
            disposed := true

    let runHttp (f: HttpRequestHeaders -> AsyncSeq<ArraySegment<byte>> -> string * seq<string * string> * byte[]) (client: Tcp.Client) =
        // TODO: Remove this value and replace with the actual value from the parser.
        let keepAlive = false
        // Create an HTTP parser.
//        let parserDelegate = ParserDelegate(onHeaders = (fun (headers, keepAlive) -> headers(headers, keepAlive, client)), 
//                                            requestBody = (fun data -> (body(data, client))), 
//                                            requestEnded = (fun req -> (requestEnd(req, client))))
//        let parser = HttpParser(parserDelegate)
    
        // Receive in a loop until the headers are read.
//        parser.Execute(new ArraySegment<_>(data)) |> ignore

        // Create an AsyncSeq of the remaining content to be read.
//        parser.Execute(new ArraySegment<_>(data)) |> ignore

        // Execute the provided function on the request and content.
        let response = Array.empty

        // Send the response.
        async {
            do! client.AsyncSend(response)
    
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

