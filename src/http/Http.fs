module Fracture.Http.Http

open System
open System.Xml.Linq
open FParsec
open FParsec.Primitives
open FParsec.CharParsers
open FSharpx
open Primitives
open CharParsers
open Uri

type HttpRequestMethod
  = OPTIONS
  | GET
  | HEAD
  | POST
  | PUT
  | DELETE
  | TRACE
  | CONNECT
  | ExtensionMethod of string
  with
  override x.ToString() =
    match x with
    | OPTIONS -> "OPTIONS"
    | GET     -> "GET"
    | HEAD    -> "HEAD"
    | POST    -> "POST"
    | PUT     -> "PUT"
    | DELETE  -> "DELETE"
    | TRACE   -> "TRACE"
    | CONNECT -> "CONNECT"
    | ExtensionMethod v -> v
type HttpVersion = int * int
type HttpRequestLine = HttpRequestMethod * UriKind * HttpVersion
type HttpStatusLine = int * string

type HttpGeneralHeader
  = CacheControl of string
  | Connection of string
  | Date of string
  | Pragma of string
  | Trailer of string
  | TransferEncoding of string
  | Upgrade of string
  | Via of string
  | Warning of string
type HttpRequestHeader
  = Accept of string
  | AcceptCharset of string
  | AcceptEncoding of string
  | AcceptLanguage of string
  | Authorization of string
  | Expect of string
  | From of string
  | Host of string
  | IfMatch of string
  | IfModifiedSince of string
  | IfNoneMatch of string
  | IfRange of string
  | IfUnmodifiedSince of string
  | MaxForwards of string
  | ProxyAuthorization of string
  | Range of string
  | Referrer of string
  | TE of string
  | UserAgent of string
type HttpContentHeader
  = ContentType of string
type HttpHeader
  = HttpGeneralHeader of HttpGeneralHeader
  | HttpRequestHeader of HttpRequestHeader
  | HttpContentHeader of HttpContentHeader
  | ExtensionHeader of string * string
  with
  override x.ToString() =
    match x with
    | HttpGeneralHeader v  -> v.ToString()
    | HttpRequestHeader v  -> v.ToString()
    | HttpContentHeader v  -> v.ToString()
    | ExtensionHeader(k,v) -> k + ": " + v

type HttpMessageBody
  = EmptyBody
  | StreamBody of System.IO.Stream
  | ByteArrayBody of byte[]
  | StringBody of string
  | JsonBody of string
  | XmlBody of XElement
  | UrlFormEncodedBody of (string * string) list
  with
  override x.ToString() =
    match x with
    | EmptyBody            -> "Empty"
    | StreamBody v         -> v.ToString()
    | ByteArrayBody v      -> v.ToString()
    | StringBody v         -> v
    | JsonBody v           -> v
    | XmlBody v            -> v.ToString()
    | UrlFormEncodedBody v -> v.ToString()

type HttpRequestMessage = HttpRequestLine * HttpHeader list * HttpMessageBody
type HttpResponseMessage = HttpStatusLine * HttpHeader list * HttpMessageBody

let text<'a> : Parser<char, 'a> = noneOf controlChars
let lws<'a> : Parser<char, 'a> = optional skipNewline >>. many1 (space <|> tab) >>% ' '
let internal separatorChars = "()<>@,;:\\\"/[]?={} \t"
let separators<'a> : Parser<char, 'a> = anyOf separatorChars
let token<'a> : Parser<string, 'a> =
  many1Satisfy2 isAsciiLetter (isNoneOf (controlChars + separatorChars))
let quotedPair<'a> : Parser<char, 'a> = skipChar '\\' >>. anyChar

// HTTP Request Method
let internal poptions<'a> : Parser<string, 'a> = pstring "OPTIONS"
let internal pget<'a> : Parser<string, 'a> = pstring "GET"
let internal phead<'a> : Parser<string, 'a> = pstring "HEAD"
let internal ppost<'a> : Parser<string, 'a> = pstring "POST"
let internal pput<'a> : Parser<string, 'a> = pstring "PUT"
let internal pdelete<'a> : Parser<string, 'a> = pstring "DELETE"
let internal ptrace<'a> : Parser<string, 'a> = pstring "TRACE"
let internal pconnect<'a> : Parser<string, 'a> = pstring "CONNECT"
let internal mapHttpMethod = function
  | "OPTIONS" -> OPTIONS
  | "GET"     -> GET
  | "HEAD"    -> HEAD
  | "POST"    -> POST
  | "PUT"     -> PUT
  | "DELETE"  -> DELETE
  | "TRACE"   -> TRACE
  | "CONNECT" -> CONNECT
  | x -> ExtensionMethod x
let httpMethod<'a> : Parser<HttpRequestMethod, 'a> =
  poptions <|> pget <|> phead <|> ppost <|> pput <|> pdelete <|> ptrace <|> pconnect <|> token |>> mapHttpMethod

// HTTP Request URI
let httpRequestUri<'a> : Parser<UriKind, 'a> = anyUri <|> absoluteUri <|> relativeUri <|> authorityRef

// HTTP version
let skipHttpPrefix<'a> : Parser<unit, 'a> = skipString "HTTP/"
let skipDot<'a> : Parser<unit, 'a> = skipChar '.'
let httpVersion<'a> : Parser<HttpVersion, 'a> =
  pipe2 (skipHttpPrefix >>. pint32) (skipDot >>. pint32) <| fun major minor -> (major, minor)

// HTTP Request Line
let httpRequestLine<'a> : Parser<HttpRequestLine, 'a> = 
  pipe3 (httpMethod .>> skipSpace) (httpRequestUri .>> skipSpace) (httpVersion .>> skipNewline)
  <| fun x y z -> (x, y, z)

// HTTP Status Line
let httpStatusLine<'a> : Parser<HttpStatusLine, 'a> = 
  pipe2 (pint32 .>> skipSpace) (many1 text .>> skipNewline)
  <| fun x y -> (x, String.ofCharList y)

// HTTP Headers
let skipColon<'a> : Parser<unit, 'a> = skipChar ':'
// TODO: This can be improved "by consisting of either *TEXT or combinations of token, separators, and quoted-string"
let fieldContent<'a> : Parser<char, 'a> = text <|> attempt lws
let httpHeader<'a> : Parser<HttpHeader, 'a> =
  pipe2 (token .>> skipColon) (many fieldContent)
  <| fun x y -> ExtensionHeader(x, (String.ofCharList y).TrimWhiteSpace())
let httpHeaders<'a> : Parser<HttpHeader list, 'a> = sepEndBy httpHeader skipNewline

// HTTP Message Body
// TODO: create a real body parser
let httpMessageBody<'a> : Parser<HttpMessageBody, 'a> = preturn EmptyBody

// HTTP Request Message
let httpRequestMessage<'a> : Parser<HttpRequestMessage, 'a> =
  pipe4 httpRequestLine httpHeaders skipNewline httpMessageBody <| fun w x _ z -> (w, x, z)

