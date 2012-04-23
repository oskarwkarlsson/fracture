namespace Fracture.Http

open System
open System.IO
open Fracture.Pipelets

type HttpParser(name, nextStage: IPipeletInput<_>, errorStage: IPipeletInput<_>) =

  member x.Parse(stream: Stream) =
    use reader = new StreamReader(stream)

    // read the request line
    let requestLine = reader.ReadLine()

    // TODO: parse the parts of the request line

    // read headers
    let rec loop cont =
      let line = reader.ReadLine()
      if line = "\r\n" then
        cont []
      else loop <| fun tail -> line :: tail

    // TODO: parse headers
    let headers = loop id |> List.map (fun header -> let pair = header.Split(':') in pair.[0].Trim(), pair.[1].Trim())

    nextStage.Post(requestLine, headers, stream)
