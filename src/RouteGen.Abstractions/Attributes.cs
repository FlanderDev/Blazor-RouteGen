namespace RouteGen;

/// <summary>
/// Marks an interface as the shared contract for an API surface. Applied once per interface
/// on the base route segment (e.g. "api/mods"). The RouteGen generators read this interface
/// via the semantic model in both the server and client compilations.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class ApiRouteAttribute : Attribute
{
    /// <summary>The base route template, e.g. "api/mods".</summary>
    public string Template { get; }

    /// <summary>
    /// Name of the named <c>HttpClient</c> (registered via <c>IHttpClientFactory</c>) that the
    /// generated client implementation should resolve. Defaults to "Default" when not set.
    /// </summary>
    public string HttpClientName { get; set; } = "Default";

    public ApiRouteAttribute(string template) => Template = template;
}

/// <summary>Base type for the per-method HTTP-verb attributes. Not intended to be used directly.</summary>
public abstract class HttpMethodAttribute : Attribute
{
    /// <summary>Route template suffix appended to the interface-level <see cref="ApiRouteAttribute"/> template. May be null/empty.</summary>
    public string? Template { get; }

    /// <summary>The HTTP verb this attribute represents (e.g. "GET").</summary>
    public abstract string Verb { get; }

    protected HttpMethodAttribute(string? template) => Template = template;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GetAttribute : HttpMethodAttribute
{
    public override string Verb => "GET";
    public GetAttribute(string? template = null) : base(template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PostAttribute : HttpMethodAttribute
{
    public override string Verb => "POST";
    public PostAttribute(string? template = null) : base(template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PutAttribute : HttpMethodAttribute
{
    public override string Verb => "PUT";
    public PutAttribute(string? template = null) : base(template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class DeleteAttribute : HttpMethodAttribute
{
    public override string Verb => "DELETE";
    public DeleteAttribute(string? template = null) : base(template) { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    public override string Verb => "PATCH";
    public PatchAttribute(string? template = null) : base(template) { }
}

/// <summary>Marks a parameter as bound from the query string. Optional/nullable parameters are omitted from the generated client's query string when null/default.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class QueryAttribute : Attribute { }

/// <summary>Marks the (at most one) parameter serialized as the JSON request body.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class BodyAttribute : Attribute { }

/// <summary>
/// Explicit override escape hatch: binds a parameter to a specific route-template token name
/// when it differs from the parameter's own name (route parameters are inferred by name-matching by default).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class RouteAttribute : Attribute
{
    public string? Name { get; }
    public RouteAttribute(string? name = null) => Name = name;
}

/// <summary>
/// Interface-level or method-level attribute controlling generated authorization requirements.
/// Mirrors ASP.NET Core's <c>AuthorizeAttribute</c> shape closely enough for the generator to
/// re-emit it onto the generated abstract controller base's action methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, Inherited = false, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public string? Roles { get; set; }
    public string? Policy { get; set; }
}

/// <summary>Marks a method as explicitly anonymous-accessible, overriding any interface-level <see cref="AuthorizeAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class AllowAnonymousAttribute : Attribute { }

/// <summary>
/// Opt-in override for the page-route generator's default member-naming heuristic. Apply to a
/// Razor component with <c>@attribute [GeneratedPathName("ModDetail")]</c> when the default
/// derived name would be ambiguous or undesirable.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GeneratedPathNameAttribute : Attribute
{
    public string Name { get; }
    public GeneratedPathNameAttribute(string name) => Name = name;
}
