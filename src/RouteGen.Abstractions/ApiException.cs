using System;
using System.Net;

namespace RouteGen;

/// <summary>
/// Thrown by generated client implementations when an API call returns a non-success status
/// code, instead of letting a bare <see cref="System.Net.Http.HttpRequestException"/> escape
/// from <c>EnsureSuccessStatusCode()</c>. Carries the status code and the raw response body so
/// callers can inspect a structured, package-defined exception.
/// </summary>
public sealed class ApiException : Exception
{
    /// <summary>The HTTP status code returned by the server.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, if any was returned.</summary>
    public string? ResponseBody { get; }

    public ApiException(HttpStatusCode statusCode, string? responseBody)
        : base($"API call failed with status {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public ApiException(HttpStatusCode statusCode, string? responseBody, Exception innerException)
        : base($"API call failed with status {(int)statusCode} ({statusCode}).", innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
