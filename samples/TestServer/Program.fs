open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Net
open System.Text
open Fracture
open Fracture.Http
open Fracture.Http.HttpResponse

let debug (x:UnhandledExceptionEventArgs) =
    Console.WriteLine(sprintf "%A" (x.ExceptionObject :?> Exception))
    Console.ReadLine() |> ignore

System.AppDomain.CurrentDomain.UnhandledException |> Observable.add debug
let shortdate = DateTime.UtcNow.ToShortDateString

let onHeaders(headers: HttpRequestHeaders, keepAlive, server: HttpServer, connection, endPoint) =

    let response =
        status (headers.Version.Major, headers.Version.Minor) "200 OK"
        *> header ("Server", "Fracture")
        *> connectionHeader headers.Version.Minor keepAlive
        *> header ("Content-Type", "text/plain")
        *> header ("Content-Length", 12)
        *> complete "Hello world."

    server.Send(connection, response |> HttpResponse.toArray, not keepAlive)

let server = new HttpServer(headers = onHeaders, body = ignore, requestEnd = ignore)

server.Start(6667)
printfn "Http Server started"
Console.ReadKey() |> ignore
