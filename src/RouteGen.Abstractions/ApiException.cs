using System;
using System.Net;
using System.Net.Http;

namespace RouteGen.Abstractions;

/// <summary>
/// Thrown by RouteGen-generated client methods when a response has a non-success HTTP status
/// code, instead of letting <c>HttpResponseMessage.EnsureSuccessStatusCode()</c> throw a bare
/// <see cref="HttpRequestException"/>. Carries the status code and the raw response body so
/// callers can inspect a typed, package-defined exception.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpMethod method, string requestUri, HttpStatusCode statusCode, string? responseBody)
        : base(FormatMessage(method, requestUri, statusCode, responseBody))
    {
        Method = method;
        RequestUri = requestUri;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP method that was used for the failing request.</summary>
    public HttpMethod Method { get; }

    /// <summary>The request URI (relative to the named <c>HttpClient</c>'s base address).</summary>
    public string RequestUri { get; }

    /// <summary>The response's HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, if any was returned, for diagnostic purposes.</summary>
    public string? ResponseBody { get; }

    private static string FormatMessage(HttpMethod method, string requestUri, HttpStatusCode statusCode, string? body)
    {
        var snippet = string.IsNullOrEmpty(body)
            ? string.Empty
            : $" Body: {(body!.Length > 500 ? body.Substring(0, 500) + "..." : body)}";
        return $"{method} {requestUri} returned {(int)statusCode} ({statusCode}).{snippet}";
    }
}
