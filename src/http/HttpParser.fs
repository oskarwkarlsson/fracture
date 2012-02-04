namespace Fracture.Http

open System
open System.Net.Http
open FSharp.IO
open FSharpx

// `HttpParser` is a `Pipelet` that takes data from a source
// and transforms it into an `HttpRequestMessage`. It follows
// the pattern of the Haskell Conduit library.
// See http://www.yesodweb.com/book/conduit
type HttpParser() =
  // Create an internal `CircularStream` that can store 3 chunks
  // passed in from the `SocketAsyncEventArgs`.
  let parseStream = new CircularStream(4096 * 3)

  // As each chunk is passed in, so long as it does not contain
  // the \r\n\r\n sequence, pass it along to the `stream`.
  // If the end of the headers is found, pass the slice containing
  // the \r\n\r\n to the FParsec parser and feed the rest into a
  // new stream that will represent the request body stream.
