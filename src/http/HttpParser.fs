namespace Fracture.Http

open System
open FSharp.Control
open FSharp.IO
open FParsec.CharParsers
open Fracture.Pipelets

type internal HttpParserMessage
  = Chunk of ArraySegment<byte> * AsyncReplyChannel<unit>
  | EOF

// `HttpParser` is a `Pipelet` that takes data from a source
// and transforms it into an `HttpRequestMessage`. It follows
// the pattern of the Haskell Conduit library.
// See http://www.yesodweb.com/book/conduit
type HttpParser(name, chunkSize, nextStage: IPipeletInput<_>, errorStage: IPipeletInput<_>) =

  let consumer = Agent.Start(fun inbox ->
    async {
      let! stream = inbox.Receive()
      let parseResult = runParserOnStream Http.httpRequestMessage nextStage name stream System.Text.Encoding.ASCII
      match parseResult with
      | Success(request, _, _) -> nextStage.Post request
      | Failure(error, _, _) -> errorStage.Post error
    })

  let conduit = Agent.Start(fun inbox ->
    // Create an internal `CircularStream` that can store 3 chunks
    // passed in from the `SocketAsyncEventArgs`.
    let headersStream = new CircularStream(chunkSize * 3)
    consumer.Post headersStream

    // TODO: We'll push to the body stream once the headers have been
    // received and sent to the headersStream.
    //let bodyStream = new System.IO.MemoryStream()

    async {
      while true do
        let! msg = inbox.Receive()
        match msg with
        | EOF -> ()
        | Chunk(chunk, reply) ->
          reply.Reply()
  
          // TODO: As each chunk is passed in, so long as it does not contain
          // the `\r\n\r\n` sequence, pass it along to the `stream`.
          // If the end of the headers is found, pass the slice containing
          // the `\r\n\r\n` to the FParsec parser and feed the rest into a
          // new stream that will represent the request `bodyStream`.
          do! headersStream.AsyncWrite(chunk.Array, chunk.Offset, chunk.Count)
    })

  member x.AsyncPost(chunk) = conduit.PostAndAsyncReply(fun reply -> Chunk(chunk, reply))
  member x.Post(chunk) = conduit.PostAndReply(fun reply -> Chunk(chunk, reply))
  member x.PostEOF() = conduit.Post(EOF)
