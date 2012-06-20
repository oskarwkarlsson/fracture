module Fracture.Http.Tests.HttpParserTest

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text
open Fracture
open Fracture.Http
open Fracture.Pipelets
open FSharp.Control
open FSharp.IO
open NUnit.Framework
open Swensen.Unquote.Assertions

let endPoint = IPEndPoint(IPAddress.Any, 80) :> EndPoint

[<Test>]
let ``test performance of 100k parses``() =
  let message = "GET http://wizardsofsmart.net/foo HTTP/1.1\r\n\r\n"B
  let timer = System.Diagnostics.Stopwatch.StartNew()
  for x = 1 to 100000 do
    let parser = new HttpParser(ignore)
    parser.Post (ArraySegment(message))
  timer.Stop()
  let time = timer.ElapsedMilliseconds
  Console.WriteLine("Parsed 100k GET requests in {0} ms.", time)
  test <@ time < 1000L @>

[<Test>]
let ``test performance of 100k parses with HttpMachine``() =
  let message = "GET http://wizardsofsmart.net/foo HTTP/1.1\r\n\r\n"B
  let timer = System.Diagnostics.Stopwatch.StartNew()
  for x = 1 to 100000 do
    let handler = Fracture.Http.Core.ParserDelegate(ignore, ignore, ignore)
    let parser = new HttpMachine.HttpParser(handler)
    parser.Execute (ArraySegment<_>(message)) |> ignore
  timer.Stop()
  let time = timer.ElapsedMilliseconds
  Console.WriteLine("Parsed 100k GET requests in {0} ms.", time)
  test <@ time < 1000L @>

type TestRequest = {
    Name: string
    Raw: byte[]
    Method: string
    RequestUri: string
    RequestPath: string
    QueryString: string
    Fragment: string
    VersionMajor: int
    VersionMinor: int
    Headers: IDictionary<string, string>
    Body: byte[] 
    ShouldKeepAlive: bool // if the message is 1.1 and !Connection:close or message is < 1.1 and Connection:keep-alive
    ShouldFail: bool }
    with
    override x.ToString() = x.Name

[<Test>]
[<TestCaseSource("requests")>]
let ``test parser correctly parses the request``(testRequest: TestRequest) =
  use stream = new MemoryStream(testRequest.Raw)
  let parser = HttpParser(fun request ->
    try
      try
        test <@ request <> null @>
        test <@ request.Method.Method.Equals(testRequest.Method, StringComparison.InvariantCultureIgnoreCase) @>
        test <@ request.RequestUri.AbsoluteUri = testRequest.RequestUri @>
        test <@ request.RequestUri.AbsolutePath = testRequest.RequestPath @>
        test <@ request.RequestUri.Query = testRequest.QueryString @>
        test <@ request.RequestUri.Fragment = testRequest.Fragment @>
        test <@ (request.Headers |> Seq.length) = (testRequest.Headers |> Seq.length) @>
      with e -> test <@ testRequest.ShouldFail @>
    finally request.Dispose())
  parser.Post (ArraySegment(testRequest.Raw))

////  use stream = new CircularStream(20)
////
////  // simulate an asynchronous socket connection pumping data into the CircularStream
////  let source = asyncSeq {
////    yield "GET / HTTP"B
////    yield "/1.1\r\nHost:"B
////    yield " wizardsof"B
////    yield "smart.net\r"B
////    yield "\n\r\n"B
////  }
////
////  // Start pumping data into the stream.
////  // This should block and require the parser to consume before continuing.
////  stream.AsyncWriteSeq(source) |> Async.Start

let requests = [|
  { Name = "No headers no body absolute uri"
    Raw = "GET http://wizardsofsmart.net/foo HTTP/1.1\r\n\r\n"B
    Method = "GET"
    RequestUri = "http://wizardsofsmart.net/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict Seq.empty
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "Host header no body relative uri"
    Raw = "GET /foo HTTP/1.1\r\nHost: wizardsofsmart.net\r\n\r\n"B
    Method = "GET"
    RequestUri = "http://wizardsofsmart.net/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [("Host", "wizardsofsmart.net")]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "Host header no body no version"
    Raw = "GET /foo\r\nHost: wizardsofsmart.net\r\n\r\n"B
    Method = "GET"
    RequestUri = "http://wizardsofsmart.net/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 0
    VersionMinor = 9
    Headers = dict [("Host", "wizardsofsmart.net")]
    ShouldFail = true // missing version
    Body = null
    ShouldKeepAlive = false }
  { Name = "No headers no body"
    Raw = "GET /foo HTTP/1.1\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 0
    VersionMinor = 9
    Headers = dict Seq.empty
    ShouldFail = true // missing Host header for relative URI
    Body = null
    ShouldKeepAlive = false }
  { Name = "no body"
    Raw = "GET /foo HTTP/1.1\r\nHost: wizardsofsmart.net\r\nFoo: Bar\r\n\r\n"B
    Method = "GET"
    RequestUri = "http://wizardsofsmart.net/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [("Host", "wizardsofsmart.net"); ("Foo", "Bar" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "headers no body no version"
    Raw = "GET /foo\r\nHost: wizardsofsmart.net\r\nFoo: Bar\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 0
    VersionMinor = 9
    Headers = dict [("Host", "wizardsofsmart.net"); ("Foo", "Bar" )]
    ShouldFail = true // missing version
    Body = null
    ShouldKeepAlive = false }
  { Name = "query string"
    Raw = "GET /foo?asdf=jklol HTTP/1.1\r\nFoo: Bar\r\nBaz-arse: Quux\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo?asdf=jklol"
    RequestPath = "/foo"
    QueryString = "asdf=jklol"
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Baz-arse", "Quux" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "fragment"
    Raw = "POST /foo?asdf=jklol#poopz HTTP/1.1\r\nFoo: Bar\r\nBaz: Quux\r\n\r\n"B
    Method = "POST"
    RequestUri = "/foo?asdf=jklol#poopz"
    RequestPath = "/foo"
    QueryString = "asdf=jklol"
    Fragment = "poopz"
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Baz", "Quux" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "zero content length"
    Raw = "POST /foo HTTP/1.1\r\nFoo: Bar\r\nContent-Length: 0\r\n\r\n"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Content-Length", "0" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "some content length"
    Raw = "POST /foo HTTP/1.1\r\nFoo: Bar\r\nContent-Length: 5\r\n\r\nhello"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Content-Length", "5" )]
    ShouldFail = false
    Body = Encoding.UTF8.GetBytes("hello")
    ShouldKeepAlive = true }
  { Name = "1.1 get"
    Raw = "GET /foo HTTP/1.1\r\nFoo: Bar\r\nConnection: keep-alive\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Connection", "keep-alive" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "1.1 get close"
    Raw = "GET /foo HTTP/1.1\r\nFoo: Bar\r\nConnection: CLOSE\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "CoNNection", "CLOSE" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = false }
  { Name = "1.1 post"
    Raw = "POST /foo HTTP/1.1\r\nFoo: Bar\r\nContent-Length: 15\r\n\r\nhelloworldhello"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Content-Length", "15" )]
    ShouldFail = false
    Body = Encoding.UTF8.GetBytes("helloworldhello")
    ShouldKeepAlive = true }
  { Name = "1.1 post close"
    Raw = "POST /foo HTTP/1.1\r\nFoo: Bar\r\nContent-Length: 15\r\nConnection: close\r\nBaz: Quux\r\n\rldhello"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Content-Length", "15" ); ( "Connection", "close" ); ( "Baz", "Quux" )]
    ShouldFail = false
    Body = Encoding.UTF8.GetBytes("helloworldhello")
    ShouldKeepAlive = false }
  // because it has no content-length it's not keep alive anyway? TODO 
  { Name = "get connection close"
    Raw = "GET /foo?asdf=jklol#poopz HTTP/1.1\r\nFoo: Bar\r\nBaz: Quux\r\nConnection: close\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo?asdf=jklol#poopz"
    RequestPath = "/foo"
    QueryString = "asdf=jklol"
    Fragment = "poopz"
    VersionMajor = 1
    VersionMinor = 1
    Headers = dict [( "Foo", "Bar" ); ( "Baz", "Quux" ); ( "Connection", "close" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = false }
  { Name = "1.0 get"
    Raw = "GET /foo?asdf=jklol#poopz HTTP/1.0\r\nFoo: Bar\r\nBaz: Quux\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo?asdf=jklol#poopz"
    RequestPath = "/foo"
    QueryString = "asdf=jklol"
    Fragment = "poopz"
    VersionMajor = 1
    VersionMinor = 0
    Headers = dict [( "Foo", "Bar" ); ( "Baz", "Quux" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = false }
  { Name = "1.0 get keep-alive"
    Raw = "GET /foo?asdf=jklol#poopz HTTP/1.0\r\nFoo: Bar\r\nBaz: Quux\r\nConnection: keep-alive\r\n\r\n"B
    Method = "GET"
    RequestUri = "/foo?asdf=jklol#poopz"
    RequestPath = "/foo"
    QueryString = "asdf=jklol"
    Fragment = "poopz"
    VersionMajor = 1
    VersionMinor = 0
    Headers = dict [( "Foo", "Bar" ); ( "Baz", "Quux" ); ( "Connection", "keep-alive" )]
    ShouldFail = false
    Body = null
    ShouldKeepAlive = true }
  { Name = "1.0 post"
    Raw = "POST /foo HTTP/1.0\r\nFoo: Bar\r\n\r\nhelloworldhello"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 0
    Headers = dict [( "Foo", "Bar" )]
    ShouldFail = false
    Body = Encoding.UTF8.GetBytes("helloworldhello")
    ShouldKeepAlive = false }
  { Name = "1.0 post keep-alive with content length"
    Raw = "POST /foo HTTP/1.0\r\nContent-Length: 15\r\nFoo: Bar\r\nConnection: keep-alive\r\n\r\nhelloworldhello"B
    Method = "POST"
    RequestUri = "/foo"
    RequestPath = "/foo"
    QueryString = ""
    Fragment = ""
    VersionMajor = 1
    VersionMinor = 0
    Headers = dict [( "Foo", "Bar" ); ( "Connection", "keep-alive" ); ( "Content-Length", "15" )]
    ShouldFail = false
    Body = Encoding.UTF8.GetBytes("helloworldhello")
    ShouldKeepAlive = true }
|]
