using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RouteGen.Generators;

/// <summary>
/// Builds an <see cref="ApiInterfaceModel"/> from an <c>[ApiRoute]</c>-decorated interface symbol,
/// via the semantic model (works whether the interface is declared in the current compilation's
/// source, or referenced from another project/assembly), and reports RouteGen diagnostics.
/// </summary>
internal static class ApiInterfaceReader
{
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly HashSet<SpecialType> SimpleSpecialTypes =
    [
        SpecialType.System_String, SpecialType.System_Boolean, SpecialType.System_Byte,
        SpecialType.System_SByte, SpecialType.System_Int16, SpecialType.System_UInt16,
        SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64,
        SpecialType.System_UInt64, SpecialType.System_Single, SpecialType.System_Double,
        SpecialType.System_Decimal, SpecialType.System_Char,
    ];

    public static ApiInterfaceModel? TryParse(
        INamedTypeSymbol interfaceSymbol,
        List<Diagnostic> diagnostics)
    {
        var apiRouteAttr = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "RouteGen.ApiRouteAttribute");
        if (apiRouteAttr is null) return null;

        var model = new ApiInterfaceModel(
            @namespace: interfaceSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : interfaceSymbol.ContainingNamespace.ToDisplayString(),
            interfaceName: interfaceSymbol.Name,
            baseRoute: apiRouteAttr.ConstructorArguments.Length > 0
                ? apiRouteAttr.ConstructorArguments[0].Value as string ?? ""
                : "");

        foreach (var namedArg in apiRouteAttr.NamedArguments)
        {
            if (namedArg.Key == "HttpClientName" && namedArg.Value.Value is string hc)
                model.HttpClientName = hc;
        }

        var (ifaceAuth, ifaceRoles, ifacePolicy) = ReadAuthorize(interfaceSymbol.GetAttributes());
        model.InterfaceLevelAuthorize = ifaceAuth;
        model.InterfaceLevelRoles = ifaceRoles;
        model.InterfaceLevelPolicy = ifacePolicy;

        // Include members from any inherited partial-interface members too (interface can be
        // partial across files; GetMembers already returns the merged member list).
        foreach (var member in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary) continue;

            var methodModel = ParseMethod(member, model, diagnostics);
            if (methodModel is not null)
                model.Methods.Add(methodModel);
        }

        DetectRouteCollisions(model, diagnostics);

        return model;
    }

    private static ApiMethodModel? ParseMethod(
        IMethodSymbol method,
        ApiInterfaceModel owner,
        List<Diagnostic> diagnostics)
    {
        HttpVerbInfo? verbInfo = null;
        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            string? verb = name switch
            {
                "RouteGen.GetAttribute" => "GET",
                "RouteGen.PostAttribute" => "POST",
                "RouteGen.PutAttribute" => "PUT",
                "RouteGen.DeleteAttribute" => "DELETE",
                "RouteGen.PatchAttribute" => "PATCH",
                _ => null
            };
            if (verb is null) continue;

            string? suffix = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string
                : null;
            verbInfo = new HttpVerbInfo(verb, suffix);
            break;
        }

        if (verbInfo is null)
            return null; // not an API operation (shouldn't normally happen on an [ApiRoute] interface, but be defensive)

        var (methodAuth, roles, policy) = ReadAuthorize(method.GetAttributes());
        bool allowAnonymous = method.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "RouteGen.AllowAnonymousAttribute");

        var methodModel = new ApiMethodModel(method.Name, verbInfo.Value.Verb, verbInfo.Value.Suffix)
        {
            HasAuthorize = methodAuth && !allowAnonymous,
            Roles = roles,
            Policy = policy,
            AllowAnonymous = allowAnonymous,
        };

        // If the method itself has no [Authorize] but the interface does, and the method isn't
        // [AllowAnonymous], inherit the interface-level authorization.
        if (!methodAuth && !allowAnonymous && owner.InterfaceLevelAuthorize)
        {
            methodModel.HasAuthorize = true;
            methodModel.Roles = owner.InterfaceLevelRoles;
            methodModel.Policy = owner.InterfaceLevelPolicy;
        }

        // Return type
        if (method.ReturnType is INamedTypeSymbol { Name: "Task" } taskType)
        {
            if (taskType.IsGenericType)
            {
                var arg = taskType.TypeArguments[0];
                methodModel.ResponseTypeFullName = arg.ToDisplayString(FullyQualified);
                methodModel.IsStreamResponse = arg.Name == "Stream";
            }
            else
            {
                methodModel.ResponseTypeFullName = null; // no body
            }
        }
        else
        {
            // Non-Task return types aren't part of the supported contract; still emit best-effort.
            methodModel.ResponseTypeFullName = method.ReturnType.ToDisplayString(FullyQualified);
        }

        string combinedTemplate = RouteTemplateParser.Combine(owner.BaseRoute, verbInfo.Value.Suffix);
        var tokens = RouteTemplateParser.ExtractTokens(combinedTemplate);

        var matchedTokenNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        bool sawBody = false;

        foreach (var param in method.Parameters)
        {
            var paramType = param.Type;
            bool isCancellationToken = paramType.ToDisplayString(FullyQualified) == "global::System.Threading.CancellationToken";

            var paramModel = new ApiParameterModel(param.Name, paramType.ToDisplayString(FullyQualified))
            {
                IsNullableOrOptional = param.HasExplicitDefaultValue || paramType.NullableAnnotation == NullableAnnotation.Annotated,
                HasDefaultValue = param.HasExplicitDefaultValue,
                DefaultValueLiteral = param.HasExplicitDefaultValue ? FormatDefault(param) : null,
            };

            if (isCancellationToken)
            {
                paramModel.Kind = ParameterKind.CancellationToken;
                methodModel.Parameters.Add(paramModel);
                continue;
            }

            bool isQuery = param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "RouteGen.QueryAttribute");
            bool isBody = param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "RouteGen.BodyAttribute");
            var routeOverride = param.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "RouteGen.RouteAttribute");

            string? overrideTokenName = null;
            if (routeOverride is not null && routeOverride.ConstructorArguments.Length > 0)
                overrideTokenName = routeOverride.ConstructorArguments[0].Value as string;

            string tokenNameToMatch = overrideTokenName ?? param.Name;
            var matchedToken = tokens.FirstOrDefault(t =>
                string.Equals(t.Name, tokenNameToMatch, System.StringComparison.OrdinalIgnoreCase));
            bool matches = tokens.Any(t => string.Equals(t.Name, tokenNameToMatch, System.StringComparison.OrdinalIgnoreCase));

            if (isBody)
            {
                if (sawBody)
                    diagnostics.Add(Diagnostic.Create(RouteGenDiagnostics.MultipleBodyParameters, GetLocation(method), method.Name));
                sawBody = true;
                paramModel.Kind = ParameterKind.Body;

                if (methodModel.Verb is "GET" or "DELETE")
                    diagnostics.Add(Diagnostic.Create(RouteGenDiagnostics.BodyOnNonBodyVerb, GetLocation(param), param.Name, method.Name, methodModel.Verb));
            }
            else if (isQuery)
            {
                paramModel.Kind = ParameterKind.Query;
                CheckSimpleType(paramType, param, method, diagnostics);
            }
            else if (matches)
            {
                paramModel.Kind = ParameterKind.RouteOrAuto;
                paramModel.MatchesRouteToken = true;
                paramModel.RouteTokenNameOverride = overrideTokenName;
                paramModel.RouteConstraint = matchedToken.Constraint;
                matchedTokenNames.Add(tokenNameToMatch);
                CheckSimpleType(paramType, param, method, diagnostics);
            }
            else
            {
                diagnostics.Add(Diagnostic.Create(RouteGenDiagnostics.UnmatchedParameter, GetLocation(param), param.Name, method.Name));
                // Fall back to treating it as a query parameter so the generator can still emit something usable.
                paramModel.Kind = ParameterKind.Query;
            }

            methodModel.Parameters.Add(paramModel);
        }

        foreach (var token in tokens)
        {
            if (!matchedTokenNames.Contains(token.Name) &&
                !methodModel.Parameters.Any(p => p.MatchesRouteToken &&
                    string.Equals(p.RouteTokenNameOverride ?? p.Name, token.Name, System.StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Diagnostic.Create(RouteGenDiagnostics.UnmatchedRouteToken, GetLocation(method), combinedTemplate, method.Name, token.Name));
            }
        }

        return methodModel;
    }

    private static void CheckSimpleType(ITypeSymbol type, IParameterSymbol param, IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var underlying = type;
        if (underlying is INamedTypeSymbol { Name: "Nullable", IsGenericType: true } nullable)
            underlying = nullable.TypeArguments[0];

        bool ok = underlying.TypeKind == TypeKind.Enum
            || SimpleSpecialTypes.Contains(underlying.SpecialType)
            || underlying.ToDisplayString(FullyQualified) is
                "global::System.Guid" or "global::System.DateTime" or "global::System.DateTimeOffset"
                or "global::System.TimeSpan" or "global::System.DateOnly" or "global::System.TimeOnly";

        if (!ok)
        {
            diagnostics.Add(Diagnostic.Create(
                RouteGenDiagnostics.UnsupportedSimpleType, GetLocation(param), param.Name, method.Name, type.ToDisplayString()));
        }
    }

    private static void DetectRouteCollisions(ApiInterfaceModel model, List<Diagnostic> diagnostics)
    {
        var seen = new Dictionary<string, ApiMethodModel>();
        foreach (var m in model.Methods)
        {
            string key = m.Verb + " " + RouteTemplateParser.Combine(model.BaseRoute, m.RouteSuffix).ToLowerInvariant();
            if (seen.TryGetValue(key, out var existing))
            {
                diagnostics.Add(Diagnostic.Create(
                    RouteGenDiagnostics.RouteCollision, Location.None,
                    existing.Name, m.Name, model.InterfaceName, m.Verb, RouteTemplateParser.Combine(model.BaseRoute, m.RouteSuffix)));
            }
            else
            {
                seen[key] = m;
            }
        }
    }

    private static (bool authorize, string? roles, string? policy) ReadAuthorize(ImmutableArray<AttributeData> attributes)
    {
        var attr = attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "RouteGen.AuthorizeAttribute");
        if (attr is null) return (false, null, null);

        string? roles = null, policy = null;
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "Roles") roles = na.Value.Value as string;
            if (na.Key == "Policy") policy = na.Value.Value as string;
        }
        return (true, roles, policy);
    }

    private static string? FormatDefault(IParameterSymbol param)
    {
        if (!param.HasExplicitDefaultValue) return null;
        var value = param.ExplicitDefaultValue;
        if (value is null) return "default";
        if (value is string s) return "\"" + s.Replace("\"", "\\\"") + "\"";
        if (value is bool b) return b ? "true" : "false";
        if (value is char c) return "'" + c + "'";
        return value.ToString();
    }

    private static Location GetLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault() ?? Location.None;

    private readonly struct HttpVerbInfo
    {
        public string Verb { get; }
        public string? Suffix { get; }
        public HttpVerbInfo(string verb, string? suffix) { Verb = verb; Suffix = suffix; }
    }
}
