using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using RouteGen.Generators.Diagnostics;
using RouteGen.Generators.Model;
using Location = RouteGen.Generators.Model.Location;

namespace RouteGen.Generators.Parsing;

internal static class ApiSurfaceParser
{
    private static readonly HashSet<string> SimpleTypeNames = new()
    {
        "System.String", "System.Guid", "System.DateTime", "System.DateTimeOffset",
        "System.TimeSpan", "System.Boolean", "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal",
        "System.Char",
    };

    public static bool TryParse(
        INamedTypeSymbol interfaceSymbol,
        out ApiInterfaceModel? model,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        var apiRouteAttr = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "RouteGen.Abstractions.ApiRouteAttribute");
        if (apiRouteAttr is null)
        {
            model = null;
            diagnostics = diags.ToImmutable();
            return false;
        }

        var baseTemplate = apiRouteAttr.ConstructorArguments.Length > 0
            ? apiRouteAttr.ConstructorArguments[0].Value as string ?? string.Empty
            : string.Empty;
        var httpClientName = apiRouteAttr.NamedArguments
            .Where(kvp => kvp.Key == "HttpClientName")
            .Select(kvp => kvp.Value.Value as string)
            .FirstOrDefault();

        var methodModels = ImmutableArray.CreateBuilder<ApiMethodModel>();
        var seenRoutes = new Dictionary<(string Verb, string Route), IMethodSymbol>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            // Property/event accessor methods (get_X, add_X, ...) are reported once, against
            // the property/event itself, not against each synthesized accessor method.
            if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
            {
                continue;
            }

            if (member is IPropertySymbol or IEventSymbol)
            {
                diags.Add(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidInterfaceShape,
                    ToRoslynLocation(member.Locations.FirstOrDefault()),
                    member.Name, interfaceSymbol.Name));
                continue;
            }

            if (member is not IMethodSymbol method)
            {
                continue; // nested types etc. — nothing to validate
            }

            if (!TryParseMethod(method, baseTemplate, diags, out var methodModel))
            {
                continue;
            }

            var key = (methodModel!.HttpVerb, methodModel.ResolvedRouteTemplate);
            if (seenRoutes.TryGetValue(key, out var existing))
            {
                diags.Add(Diagnostic.Create(
                    DiagnosticDescriptors.RouteCollision,
                    ToRoslynLocation(method.Locations.FirstOrDefault()),
                    existing.Name, method.Name, interfaceSymbol.Name, methodModel.HttpVerb, methodModel.ResolvedRouteTemplate));
            }
            else
            {
                seenRoutes[key] = method;
            }

            methodModels.Add(methodModel);
        }

        model = new ApiInterfaceModel(
            InterfaceName: interfaceSymbol.Name,
            Namespace: interfaceSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : interfaceSymbol.ContainingNamespace.ToDisplayString(),
            BaseRouteTemplate: baseTemplate,
            HttpClientName: httpClientName,
            Methods: methodModels.ToImmutable(),
            InterfaceLocation: ToLocation(interfaceSymbol.Locations.FirstOrDefault()));
        diagnostics = diags.ToImmutable();
        return true;
    }

    private static bool TryParseMethod(
        IMethodSymbol method,
        string baseTemplate,
        ImmutableArray<Diagnostic>.Builder diags,
        out ApiMethodModel? result)
    {
        result = null;

        var verbAttr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.BaseType?.ToDisplayString()
            == "RouteGen.Abstractions.HttpMethodAttribute" || IsHttpVerbAttribute(a));
        if (verbAttr is null)
        {
            diags.Add(Diagnostic.Create(
                DiagnosticDescriptors.MissingHttpMethodAttribute,
                ToRoslynLocation(method.Locations.FirstOrDefault()),
                method.Name, method.ContainingType.Name));
            return false;
        }

        var verb = verbAttr.AttributeClass!.Name switch
        {
            "GetAttribute" => "GET",
            "PostAttribute" => "POST",
            "PutAttribute" => "PUT",
            "DeleteAttribute" => "DELETE",
            "PatchAttribute" => "PATCH",
            _ => "GET",
        };
        var suffix = verbAttr.ConstructorArguments.Length > 0 ? verbAttr.ConstructorArguments[0].Value as string : null;
        var resolvedTemplate = RouteTemplate.Combine(baseTemplate, suffix);
        var tokens = RouteTemplate.ParseTokens(resolvedTemplate).ToDictionary(t => t.Name, t => t);

        var canHaveBody = verb is "POST" or "PUT" or "PATCH";

        var parameterModels = ImmutableArray.CreateBuilder<ApiParameterModel>();
        var matchedTokenNames = new HashSet<string>();
        ApiParameterModel? bodyParam = null;
        var duplicateBodyReported = false;

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var p = method.Parameters[i];
            var isLastCancellationToken = i == method.Parameters.Length - 1
                && p.Type.ToDisplayString() == "System.Threading.CancellationToken";

            if (isLastCancellationToken)
            {
                parameterModels.Add(new ApiParameterModel(
                    p.Name, p.Type.ToDisplayString(), false, p.HasExplicitDefaultValue, FormatDefaultValueLiteral(p),
                    ParameterKind.CancellationToken, null, true, true));
                continue;
            }

            var hasQuery = p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "RouteGen.Abstractions.QueryAttribute");
            var hasBody = p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "RouteGen.Abstractions.BodyAttribute");
            var routeOverride = p.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "RouteGen.Abstractions.RouteAttribute");
            var explicitTokenName = routeOverride?.ConstructorArguments.FirstOrDefault().Value as string;

            var tokenName = explicitTokenName ?? p.Name;
            var isRouteToken = tokens.ContainsKey(tokenName);

            if (hasBody && !canHaveBody)
            {
                diags.Add(Diagnostic.Create(
                    DiagnosticDescriptors.BodyOnNonBodyVerb,
                    ToRoslynLocation(p.Locations.FirstOrDefault()),
                    p.Name, method.Name, verb));
            }

            var isSimple = IsSimpleType(p.Type);

            ParameterKind kind;
            string? resolvedTokenName = null;

            if (isRouteToken)
            {
                kind = ParameterKind.Route;
                resolvedTokenName = tokenName;
                matchedTokenNames.Add(tokenName);
                if (!isSimple)
                {
                    diags.Add(Diagnostic.Create(DiagnosticDescriptors.ComplexTypeInUrl, ToRoslynLocation(p.Locations.FirstOrDefault()), p.Name, method.Name, p.Type.ToDisplayString()));
                }
            }
            else if (hasBody)
            {
                kind = ParameterKind.Body;
                if (bodyParam is not null && !duplicateBodyReported)
                {
                    diags.Add(Diagnostic.Create(
                        DiagnosticDescriptors.MultipleBodyParameters,
                        ToRoslynLocation(method.Locations.FirstOrDefault()),
                        method.Name, bodyParam.Name, p.Name));
                    duplicateBodyReported = true;
                }
            }
            else if (hasQuery)
            {
                kind = ParameterKind.Query;
                if (!isSimple)
                {
                    diags.Add(Diagnostic.Create(DiagnosticDescriptors.ComplexTypeInUrl, ToRoslynLocation(p.Locations.FirstOrDefault()), p.Name, method.Name, p.Type.ToDisplayString()));
                }
            }
            else
            {
                diags.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnmatchedMethodParameter,
                    ToRoslynLocation(p.Locations.FirstOrDefault()),
                    p.Name, method.Name, resolvedTemplate));
                kind = ParameterKind.Query; // best-effort fallback so downstream emission doesn't crash
            }

            var paramModel = new ApiParameterModel(
                p.Name,
                p.Type.ToDisplayString(),
                p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.IsReferenceType && p.HasExplicitDefaultValue && p.ExplicitDefaultValue is null,
                p.HasExplicitDefaultValue,
                FormatDefaultValueLiteral(p),
                kind,
                resolvedTokenName,
                false,
                isSimple);

            if (kind == ParameterKind.Body)
            {
                bodyParam = paramModel;
            }

            parameterModels.Add(paramModel);
        }

        foreach (var token in tokens.Values)
        {
            if (!matchedTokenNames.Contains(token.Name))
            {
                diags.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnmatchedRouteToken,
                    ToRoslynLocation(method.Locations.FirstOrDefault()),
                    resolvedTemplate, method.Name, token.Name));
            }
        }

        var (returnKind, unwrapped) = ClassifyReturnType(method.ReturnType);

        var authAttrs = method.GetAttributes()
            .Where(a => a.AttributeClass?.Name is "AuthorizeAttribute" or "AllowAnonymousAttribute")
            .Select(ToAttributeUsageModel)
            .ToImmutableArray();

        var httpClientNameOverride = method.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "RouteGen.Abstractions.HttpClientNameAttribute")
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
            .FirstOrDefault();

        result = new ApiMethodModel(
            method.Name,
            verb,
            suffix,
            resolvedTemplate,
            method.ReturnType.ToDisplayString(),
            returnKind,
            unwrapped,
            parameterModels.ToImmutable(),
            authAttrs,
            httpClientNameOverride,
            ToLocation(method.Locations.FirstOrDefault()));
        return true;
    }

    private static bool IsHttpVerbAttribute(AttributeData a) =>
        a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "RouteGen.Abstractions"
        && a.AttributeClass?.Name is "GetAttribute" or "PostAttribute" or "PutAttribute" or "DeleteAttribute" or "PatchAttribute";

    private static (ApiMethodReturnKind, string?) ClassifyReturnType(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
        {
            return (ApiMethodReturnKind.TaskVoid, null);
        }

        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } named)
        {
            var inner = named.TypeArguments[0];
            var innerDisplay = inner.ToDisplayString();
            if (innerDisplay is "System.IO.Stream" or "System.IO.Stream?")
            {
                return (ApiMethodReturnKind.TaskOfStream, innerDisplay);
            }

            return (ApiMethodReturnKind.TaskOfJson, innerDisplay);
        }

        // Unsupported shape; treat as void-task so we don't crash — RG0007 is reserved for
        // non-method members, but a bad return type still degrades gracefully here.
        return (ApiMethodReturnKind.TaskVoid, null);
    }

    private static AttributeUsageModel ToAttributeUsageModel(AttributeData a)
    {
        var ctorArgs = a.ConstructorArguments.Select(FormatTypedConstant).ToImmutableArray();
        var namedArgs = a.NamedArguments.Select(kvp => (kvp.Key, FormatTypedConstant(kvp.Value))).ToImmutableArray();
        return new AttributeUsageModel(a.AttributeClass!.Name, ctorArgs, namedArgs);
    }

    private static string FormatTypedConstant(TypedConstant tc)
    {
        if (tc.Value is string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        if (tc.Value is null)
        {
            return "null";
        }

        return tc.Value.ToString() ?? "null";
    }

    private static string? FormatDefaultValueLiteral(IParameterSymbol p)
    {
        if (!p.HasExplicitDefaultValue)
        {
            return null;
        }

        var v = p.ExplicitDefaultValue;
        return v switch
        {
            null => "default",
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            bool b => b ? "true" : "false",
            char c => "'" + c + "'",
            _ => v.ToString(),
        };
    }

    private static bool IsSimpleType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type is INamedTypeSymbol { Name: "Nullable", TypeArguments.Length: 1 } nullable)
        {
            return IsSimpleType(nullable.TypeArguments[0]);
        }

        var display = type.OriginalDefinition.ToDisplayString();
        return SimpleTypeNames.Contains(display.TrimEnd('?'));
    }

    private static Microsoft.CodeAnalysis.Location ToRoslynLocation(Microsoft.CodeAnalysis.Location? location) =>
        location ?? Microsoft.CodeAnalysis.Location.None;

    private static Location ToLocation(Microsoft.CodeAnalysis.Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return Location.None;
        }

        var span = location.GetLineSpan();
        return new Location(span.Path, span.StartLinePosition.Line, span.StartLinePosition.Character);
    }
}
