﻿open System
open System.Collections.Generic
open System.Diagnostics
open System.Net
open Fracture.Http
open Fracture.Common

let debug (x:UnhandledExceptionEventArgs) =
    Console.WriteLine(sprintf "%A" (x.ExceptionObject :?> Exception))
    Console.ReadLine() |> ignore

System.AppDomain.CurrentDomain.UnhandledException |> Observable.add debug
let shortdate = DateTime.UtcNow.ToShortDateString

let response = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\nConnection: Keep-Alive\r\nContent-Length: 12\r\nServer: Fracture\r\n\r\nHello world."
// NOTE: This demo never listens to the request body.
let server = new HttpServer(headers = (fun (headers, svr, sd) -> svr.Send(sd.RemoteEndPoint, response, headers.KeepAlive) ), 
                            body = (fun(body, svr, sd) -> () ), 
                            requestEnd = fun(req, svr, sd) -> () )

server.Start(6667)
printfn "Http Server started"
Console.ReadKey() |> ignore
