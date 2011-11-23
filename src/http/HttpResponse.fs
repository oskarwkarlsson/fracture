module Fracture.Http.HttpResponse

open System.IO
open System.Text
open FSharpx.Reader

(* Simple HTTP Response DSL *)

type HttpResponse = Reader<TextWriter, unit>
let respond = reader
let empty = respond.Zero<TextWriter>()

/// Pipe operator which allows chaining response builder functions.
let inline ( *>) x y = map2 (fun _ z -> z) x y

let status (major, minor) statusCode : HttpResponse =
    fun writer -> writer.WriteLine("HTTP/{0}.{1} {2}", major, minor, statusCode)

let header (key: string, value) : HttpResponse =
    fun writer -> writer.WriteLine(key + ": " + value.ToString())

let connectionHeader minor keepAlive : HttpResponse =
    if keepAlive && minor = 0 then
        header ("Connection", "Keep-Alive")
    elif not keepAlive && minor = 1 then
        header ("Connection", "Close")
    else respond.Zero()

let complete (content: byte[]) : HttpResponse =
    fun writer ->
        writer.WriteLine()
        if content <> null && content.Length > 0 then
            writer.Write(content)
        writer.Flush()

let toArray response =
    use stream = new MemoryStream()
    // Default is to ASCII, which is standard for HTTP messages.
    // In general, HTTP headers should be safe in any encoding.
    use writer = new StreamWriter(stream, Encoding.ASCII) :> TextWriter
    response writer
    stream.GetBuffer()
    
let toEncoded encoding response =
    use stream = new MemoryStream()
    use writer = new StreamWriter(stream, encoding) :> TextWriter
    response writer
    stream.GetBuffer()

let toString response =
    let sb = StringBuilder()
    use writer = new StringWriter(sb) :> TextWriter
    response writer
    sb.ToString()
