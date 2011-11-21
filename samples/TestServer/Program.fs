open System
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Text
open Fracture
open Fracture.Sockets
open HttpMachine

let debug (x:UnhandledExceptionEventArgs) =
    Console.WriteLine(sprintf "%A" (x.ExceptionObject :?> Exception))
    Console.ReadLine() |> ignore

System.AppDomain.CurrentDomain.UnhandledException |> Observable.add debug
let shortdate = DateTime.UtcNow.ToShortDateString
open Fracture.HttpServer

let status (major, minor) statusCode (sb: StringBuilder) =
    sb.AppendFormat("HTTP/{0}.{1} {2}", major, minor, statusCode).AppendLine()

let header (key, value) (sb: StringBuilder) = sb.AppendLine(key + ": " + value.ToString())

let connectionHeader minor keepAlive (sb: StringBuilder) =
    if keepAlive then
        if minor = 0 then
            sb |> header ("Connection", "Keep-Alive")
        else sb
    else
        if minor = 1 then
            sb |> header ("Connection", "Close")
        else sb

let complete (content: byte[]) (sb: StringBuilder) =
    sb.AppendLine() |> ignore
    if content <> null && content.Length > 0 then
        sb.Append(content).ToString()
    else sb.ToString()

let onHeaders(headers: HttpRequestHeaders, keepAlive, server: HttpServer, connection, endPoint) =

    let response =
        StringBuilder()
        |> status (headers.Version.Major, headers.Version.Minor) "200 OK"
        |> header ("Server", "Fracture")
        |> connectionHeader headers.Version.Minor keepAlive
        |> header ("Content-Type", "text/plain")
        |> header ("Content-Length", 12)
        |> complete "Hello world."B

    server.Send(connection, response, not keepAlive)

let server = new HttpServer(headers = onHeaders, body = ignore, requestEnd = ignore)

server.Start(6667)
printfn "Http Server started"
Console.ReadKey() |> ignore
