module Fracture.Http.HttpResponse

open System.Text

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
