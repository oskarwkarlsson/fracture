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

let app headers content =
    status (headers.Version.Major, headers.Version.Minor) "200 OK"
    *> header ("Server", "Fracture")
    *> connectionHeader headers.Version.Minor headers.KeepAlive
    *> header ("Content-Type", "text/plain")
    *> header ("Content-Length", 12)
    *> complete "Hello world."

app |> HttpListener.Start(IPAddress.Any, 6667)

printfn "Http Server started"

Console.ReadKey() |> ignore
