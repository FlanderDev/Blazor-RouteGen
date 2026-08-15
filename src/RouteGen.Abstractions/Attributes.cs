using System;

namespace RouteGen.Abstractions;

/// <summary>
/// Declares the base route for every operation on the interface, and (optionally) which
/// named <c>HttpClient</c> the generated client implementation should resolve via
/// <c>IHttpClientFactory</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class ApiRouteAttribute : Attribute
{
    /// <param name="template">
    /// Base route template, e.g. "api/mods". Individual methods append a suffix to this.
    /// </param>
    public ApiRouteAttribute(string template)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
    }

    /// <summary>The base route template for this API surface.</summary>
    public string Template { get; }

    /// <summary>
    /// Name of the <c>HttpClient</c> to resolve via <c>IHttpClientFactory.CreateClient(name)</c>
    /// in the generated client implementation. Defaults to "" (the factory's default client)
    /// when not set. Can be overridden per-method with <see cref="HttpClientNameAttribute"/>.
    /// </summary>
    public string? HttpClientName { get; set; }
}

/// <summary>
/// Overrides the named <c>HttpClient</c> used for a single method, when it differs from the
/// interface-level <see cref="ApiRouteAttribute.HttpClientName"/> (e.g. a login endpoint that
/// lives outside the "api/*" base address).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HttpClientNameAttribute : Attribute
{
    public HttpClientNameAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>Base class for the HTTP-verb attributes below. Not intended for direct use.</summary>
public abstract class HttpMethodAttribute : Attribute
{
    protected HttpMethodAttribute(string verb, string? template)
    {
        Verb = verb;
        Template = template;
    }

    /// <summary>The HTTP verb, e.g. "GET".</summary>
    public string Verb { get; }

    /// <summary>
    /// Route-template suffix appended to the interface-level <see cref="ApiRouteAttribute.Template"/>.
    /// May be null/empty when the method's route is exactly the interface's base route.
    /// </summary>
    public string? Template { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GetAttribute : HttpMethodAttribute
{
    public GetAttribute() : base("GET", null) { }
    public GetAttribute(string template) : base("GET", template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PostAttribute : HttpMethodAttribute
{
    public PostAttribute() : base("POST", null) { }
    public PostAttribute(string template) : base("POST", template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PutAttribute : HttpMethodAttribute
{
    public PutAttribute() : base("PUT", null) { }
    public PutAttribute(string template) : base("PUT", template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class DeleteAttribute : HttpMethodAttribute
{
    public DeleteAttribute() : base("DELETE", null) { }
    public DeleteAttribute(string template) : base("DELETE", template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    public PatchAttribute() : base("PATCH", null) { }
    public PatchAttribute(string template) : base("PATCH", template) { }
}

/// <summary>
/// Marks a parameter as a query-string parameter. Parameters that already appear as a
/// <c>{name}</c> token in the route template are treated as route parameters automatically
/// and should not be marked <see cref="QueryAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class QueryAttribute : Attribute { }

/// <summary>
/// Marks the (at most one) parameter that should be serialized as the JSON request body.
/// Only valid on POST/PUT/PATCH methods.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class BodyAttribute : Attribute { }

/// <summary>
/// Explicit override escape hatch: binds a parameter to a specific route-template token name
/// when the parameter name doesn't match the token, instead of relying on name-matching.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class RouteAttribute : Attribute
{
    public RouteAttribute(string tokenName) => TokenName = tokenName;
    public string TokenName { get; }
}

/// <summary>
/// Opt-in override for the member name the Blazor page-route generator would otherwise infer
/// from a <c>.razor</c> file's name/path. Apply as <c>@attribute [GeneratedPathName("ModDetail")]</c>
/// at the top of the .razor file.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GeneratedPathNameAttribute : Attribute
{
    public GeneratedPathNameAttribute(string name) => Name = name;
    public string Name { get; }
}
