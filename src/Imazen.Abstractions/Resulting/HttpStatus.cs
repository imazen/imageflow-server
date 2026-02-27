using System.Net;

namespace Imazen.Abstractions.Resulting;

// maybe switch to record struct?
public readonly struct HttpStatus(int statusCode, string? reasonPhrase)
{
    
  
    public HttpStatus(HttpStatusCode statusCode) : this((int)statusCode)
    {
    }
    public HttpStatus(int statusCode) : this(statusCode,  GetDefaultStatusCodePhrase(statusCode))
    {
    }
    public int StatusCode { get; } = statusCode;
    public string? Message { get; } = reasonPhrase ?? GetDefaultStatusCodePhrase(statusCode);


    public HttpStatus WithAddFrom(string? sourceAction) => sourceAction == null ? this : new HttpStatus(StatusCode,$"{Message} (from {sourceAction})");
    public HttpStatus WithAppend(string? appendString) => appendString == null ? this : new HttpStatus(StatusCode,$"{Message}, {appendString}");

    public HttpStatus WithMessage(string? reasonPhrase) => new HttpStatus(StatusCode, reasonPhrase);
    
    public static implicit operator HttpStatus(int statusCode) => new HttpStatus(statusCode);
    // HttpStatusCode
    public static implicit operator HttpStatus(HttpStatusCode statusCode) => new HttpStatus((int)statusCode);
    
    public static implicit operator int(HttpStatus statusCode) => statusCode.StatusCode;
    
    public static explicit operator HttpStatusCode(HttpStatus statusCode) => (HttpStatusCode)statusCode.StatusCode;

    public static implicit operator HttpStatus((int statusCode, string? reasonPhrase) statusCode) => 
        new HttpStatus(statusCode.statusCode).WithAppend(statusCode.reasonPhrase);

    public static implicit operator HttpStatus((HttpStatusCode statusCode, string? reasonPhrase) statusCode) =>
        new HttpStatus((int)statusCode.statusCode).WithAppend(statusCode.reasonPhrase);
    
    
    
    public override string ToString()
    {
        return Message == null ? StatusCode.ToString() : $"{StatusCode} {Message}";
    }
    
    // equality based on status code only — consistent with operator ==, GetHashCode, and cross-type operators
    public override bool Equals(object? obj)
    {
        if (obj is not HttpStatus other) return false;
        return StatusCode == other.StatusCode;
    }
    
    public static bool operator ==(HttpStatus left, HttpStatus right)
    {
        return left.StatusCode == right.StatusCode;
    }

    public static bool operator !=(HttpStatus left, HttpStatus right)
    {
        return !(left == right);
    }
    
    public static bool operator ==(HttpStatusCode left, HttpStatus right)
    {
        return (int)left == right.StatusCode;
    }

    public static bool operator !=(HttpStatusCode left, HttpStatus right)
    {
        return !(left == right);
    }
    public static bool operator ==(HttpStatus left, int right)
    {
        return left.StatusCode == (int)right;
    }

    public static bool operator !=(HttpStatus left, int right)
    {
        return !(left == right);
    }
    public static bool operator ==(int left, HttpStatus right)
    {
        return left == right.StatusCode;
    }
    public static bool operator !=(int left, HttpStatus right)
    {
        return !(left == right);
    }

    public override int GetHashCode()
    {
        return StatusCode.GetHashCode();
    }

    public static HttpStatus Ok => new HttpStatus(200);
    public static HttpStatus NotFound => new HttpStatus(404);
    public static HttpStatus ServerError => new HttpStatus(500);
    public static HttpStatus GatewayTimeout => new HttpStatus(504);
    public static HttpStatus NotImplemented => new HttpStatus(501);
    public static HttpStatus BadGateway => new HttpStatus(502);
    public static HttpStatus ServiceUnavailable => new HttpStatus(503);
    public static HttpStatus Unauthorized => new HttpStatus(401);
    public static HttpStatus Forbidden => new HttpStatus(403);
    public static HttpStatus BadRequest => new HttpStatus(400);

    private static string GetDefaultStatusCodePhrase(int code)
    {
        if (code < 100 || code > 599)
            throw new ArgumentOutOfRangeException(nameof(code), "Status code must be between 100 and 599");
        return code switch
        {
            100 => "100 Continue",
            101 => "101 Switching Protocols",
            102 => "102 Processing",
            103 => "103 Early Hints",
            200 => "200 OK",
            201 => "201 Created",
            202 => "202 Accepted",
            203 => "203 Non-Authoritative Information",
            204 => "204 No Content",
            205 => "205 Reset Content",
            206 => "206 Partial Content",
            207 => "207 Multi-Status",
            208 => "208 Already Reported",
            226 => "226 IM Used",
            300 => "300 Multiple Choices",
            301 => "301 Moved Permanently",
            302 => "302 Found",
            303 => "303 See Other",
            304 => "304 Not Modified",
            305 => "305 Use Proxy",
            306 => "306 Unused",
            307 => "307 Temporary Redirect",
            308 => "308 Permanent Redirect",
            400 => "400 Bad Request",
            401 => "401 Unauthorized",
            402 => "402 Payment Required",
            403 => "403 Forbidden",
            404 => "404 Not Found",
            405 => "405 Method Not Allowed",
            406 => "406 Not Acceptable",
            407 => "407 Proxy Authentication Required",
            408 => "408 Request Timeout",
            409 => "409 Conflict",
            410 => "410 Gone",
            411 => "411 Length Required",
            412 => "412 Precondition Failed",
            413 => "413 Payload Too Large",
            414 => "414 URI Too Long",
            415 => "415 Unsupported Media Type",
            416 => "416 Range Not Satisfiable",
            417 => "417 Expectation Failed",
            418 => "418 I'm a teapot",
            419 => "419 Authentication Timeout",
            421 => "421 Misdirected Request",
            422 => "422 Unprocessable Entity",
            423 => "423 Locked",
            424 => "424 Failed Dependency",
            425 => "425 Too Early",
            426 => "426 Upgrade Required",
            428 => "428 Precondition Required",
            429 => "429 Too Many Requests",
            431 => "431 Request Header Fields Too Large",
            451 => "451 Unavailable For Legal Reasons",
            500 => "500 Internal Server Error",
            501 => "501 Not Implemented",
            502 => "502 Bad Gateway",
            503 => "503 Service Unavailable",
            504 => "504 Gateway Timeout",
            505 => "505 HTTP Version",
            506 => "506 Variant Also Negotiates",
            507 => "507 Insufficient Storage",
            508 => "508 Loop Detected",
            509 => "509 Bandwidth Limit Exceeded",
            510 => "510 Not Extended",
            511 => "511 Network Authentication Required",
            _ => "Unknown Status Code"
        };
    }

}


// List the full set of properties that can be returned from a Put operation on Azure and S3
// https://docs.microsoft.com/en-us/rest/api/storageservices/put-blob
// https://docs.aws.amazon.com/AmazonS3/latest/API/RESTObjectPUT.html

// Which result codes exist? List them below in a comment
// 200 OK
// 201 Created
// 202 Accepted
// 203 Non-Authoritative Information (since HTTP/1.1)
// 204 No Content
// 205 Reset Content
// 206 Partial Content (RFC 7233)
// 207 Multi-Status (WebDAV; RFC 4918)