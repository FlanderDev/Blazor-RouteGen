using System.Collections.Generic;

namespace RouteGen.Generators;

/// <summary>Parsed representation of a single <c>[ApiRoute]</c>-decorated interface.</summary>
internal sealed class ApiInterfaceModel
{
    public string Namespace { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public string BaseRoute { get; set; } = "";
    public string HttpClientName { get; set; } = "Default";
    public bool InterfaceLevelAuthorize { get; set; }
    public string? InterfaceLevelRoles { get; set; }
    public string? InterfaceLevelPolicy { get; set; }
    public List<ApiMethodModel> Methods { get; } = new();

    /// <summary>
    /// Generated type name stem, e.g. "Mods" from "IModsApi" (strips a leading "I" and a
    /// trailing "Api" when present, so "ModsApiControllerBase" / "HttpModsApi" read naturally
    /// instead of "ModsApiApiControllerBase").
    /// </summary>
    public string ShortName
    {
        get
        {
            string name = InterfaceName;
            if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
                name = name.Substring(1);
            if (name.Length > 3 && name.EndsWith("Api"))
                name = name.Substring(0, name.Length - 3);
            return name.Length == 0 ? InterfaceName : name;
        }
    }
}

internal sealed class ApiMethodModel
{
    public string Name { get; set; } = "";
    public string Verb { get; set; } = "GET";
    public string? RouteSuffix { get; set; }
    /// <summary>Full name of the Task&lt;T&gt; type argument, or null when the return type is bare Task.</summary>
    public string? ResponseTypeFullName { get; set; }
    public bool IsStreamResponse { get; set; }
    public bool HasAuthorize { get; set; }
    public string? Roles { get; set; }
    public string? Policy { get; set; }
    public bool AllowAnonymous { get; set; }
    public List<ApiParameterModel> Parameters { get; } = new();
}

internal enum ParameterKind
{
    RouteOrAuto,
    Query,
    Body,
    CancellationToken
}

internal sealed class ApiParameterModel
{
    public string Name { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public bool IsNullableOrOptional { get; set; }
    public bool HasDefaultValue { get; set; }
    public string? DefaultValueLiteral { get; set; }
    public ParameterKind Kind { get; set; }
    public string? RouteTokenNameOverride { get; set; }
    public bool MatchesRouteToken { get; set; }
    public string? RouteConstraint { get; set; }
}
