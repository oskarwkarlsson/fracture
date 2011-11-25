namespace Fracture.Http

open System
open System.Collections.Generic
open System.Net
open System.IO
open Fracture
open FSharp.Control
open HttpMachine
open HttpResponse

type HttpApplication = HttpRequestHeaders -> ArraySegment<byte> list -> HttpResponse

type HttpListener (poolSize, perOperationBufferSize, acceptBacklogCount) =
    let listener = new Tcp.Listener(poolSize, perOperationBufferSize, acceptBacklogCount)   

    let disposed = ref false
    let cleanUp disposing =
        if not !disposed then
            if disposing then
                listener.Dispose()
            disposed := true

    let httpAgent f =
        Agent<_>.Start(fun inbox ->
            let clientState = new Dictionary<IPEndPoint, Tcp.Client * HttpRequestHeaders>()
            let rec loop() = async {
                let! msg = inbox.Receive()
                match msg with
                | Start(client) ->
                    clientState.[client.RemoteEndPoint] <- (client, HttpRequestHeaders.Default)
                    do! loop()
                | Headers(endPoint, headers) ->
                    let client, _ = clientState.[endPoint]
                    clientState.[endPoint] <- (client, headers)
                    do! loop()
                | Body(endPoint, body) ->
                    let client, headers = clientState.[endPoint]
                    do! client.AsyncSend(f headers body |> HttpResponse.toArray)
                    if not headers.KeepAlive then
                        do! client.AsyncDisconnect()
                    clientState.Remove(endPoint) |> ignore
                    do! loop() }
            loop())

    let runHttp agent client =
        let parser = HttpParser(AgentParserDelegate(agent, client))

        let rec loop (client: Tcp.Client) = async {
            let! data = client.AsyncReceive()
            if parser.Execute(data) = perOperationBufferSize then
                do! loop client }
        loop client

    static member Start(ipAddress, port) f =
        let listener = new HttpListener(30000, 1024, 10000)
        listener.Listen(ipAddress, port) f

    member this.Listen (ipAddress, port) f =
        listener.Listen(ipAddress, port) (runHttp (httpAgent f))

    member this.Dispose() =
        cleanUp true
        GC.SuppressFinalize(this)

    override this.Finalize() = cleanUp false

    interface IDisposable with
        member this.Dispose() = this.Dispose()
