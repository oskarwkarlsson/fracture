﻿//----------------------------------------------------------------------------
//
// Copyright (c) 2011-2012 Dave Thomas (@7sharp9) 
//                         Ryan Riley (@panesofglass)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//----------------------------------------------------------------------------
open System
open System.Collections.Generic
open System.Diagnostics
open System.Net
open Frack
open Owin

let debug (x:UnhandledExceptionEventArgs) =
    Console.WriteLine(sprintf "%A" (x.ExceptionObject :?> Exception))
    Console.ReadLine() |> ignore

System.AppDomain.CurrentDomain.UnhandledException |> Observable.add debug

let server = new HttpServer (fun env -> async {
    env.ResponseHeaders.Add("Content-Type", [|"text/plain"|])
    let body = "Hello, world!"B
    env.ResponseBody.Write(body, 0, body.Length)
    env.ResponseBody.Flush() // Is this necessary?
})

server.Start(6667)
printfn "Http Server started on port 6667"
Console.ReadKey() |> ignore
