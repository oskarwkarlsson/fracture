open System
open System.Net
open Fracture
open Fracture.Sockets
open HttpMachine
open System.Collections.Generic
open System.Diagnostics

let debug (x:UnhandledExceptionEventArgs) =
    Console.WriteLine(sprintf "%A" (x.ExceptionObject :?> Exception))
    Console.ReadLine() |> ignore

System.AppDomain.CurrentDomain.UnhandledException |> Observable.add debug
let shortdate = DateTime.UtcNow.ToShortDateString
open Fracture.HttpServer

let onHeaders(headers: HttpRequestHeaders, keepAlive, server: HttpServer, connection, endPoint) =
    let connectionHeader =
        if keepAlive then
            if headers.Version.Minor = 0 then
                "Connection: Keep-Alive\r\n"
            else ""
        else
            if headers.Version.Minor = 1 then
                "Connection: Close\r\n"
            else ""
    let response = sprintf "HTTP/%d.%d 200 OK\r\nContent-Type: text/plain\r\n%sContent-Length: 12\r\nServer: Fracture\r\n\r\nHello world."
                           headers.Version.Major
                           headers.Version.Minor
                           connectionHeader
    server.Send(connection, response, not keepAlive)
let server = new HttpServer(headers = onHeaders, body = ignore, requestEnd = ignore)

server.Start(6667)
printfn "Http Server started"
Console.ReadKey() |> ignore
