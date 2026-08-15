using Microsoft.CodeAnalysis;

namespace RouteGen.Generators.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "RouteGen";

    public static readonly DiagnosticDescriptor RouteCollision = new(
        id: "RG0001",
        title: "Duplicate route + HTTP verb",
        messageFormat: "Methods '{0}' and '{1}' on interface '{2}' both resolve to {3} {4}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two methods on the same interface produce an identical resulting route template and HTTP verb, which ASP.NET Core cannot disambiguate at runtime.");

    public static readonly DiagnosticDescriptor BodyOnNonBodyVerb = new(
        id: "RG0002",
        title: "[Body] used with a verb that has no request body",
        messageFormat: "Parameter '{0}' on method '{1}' is marked [Body], but [{2}] requests do not carry a request body",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Body] only makes sense on POST/PUT/PATCH methods; GET and DELETE requests should not carry a JSON body.");

    public static readonly DiagnosticDescriptor UnmatchedRouteToken = new(
        id: "RG0003",
        title: "Route template token has no matching parameter",
        messageFormat: "Route template '{0}' on method '{1}' contains token '{{{2}}}', but no method parameter matches it. Add a parameter named '{2}', or use [Route(\"{2}\")] to bind an existing one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every {token} in a route template must be matched by a method parameter (by name, or via an explicit [Route] override).");

    public static readonly DiagnosticDescriptor UnmatchedMethodParameter = new(
        id: "RG0004",
        title: "Parameter is not a route token, [Query], or [Body]",
        messageFormat: "Parameter '{0}' on method '{1}' does not appear as a token in the route template '{2}' and is not marked [Query] or [Body]. Mark it [Query], add it to the route template, or bind it explicitly with [Route]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every method parameter (other than a trailing CancellationToken) must be explicitly accounted for as a route parameter, a query parameter, or the request body.");

    public static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        id: "RG0005",
        title: "More than one [Body] parameter",
        messageFormat: "Method '{0}' has more than one parameter marked [Body] ('{1}' and '{2}'); only one parameter may be the request body",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request can only have a single JSON body; mark at most one parameter [Body].");

    public static readonly DiagnosticDescriptor ComplexTypeInUrl = new(
        id: "RG0006",
        title: "Complex type used as a route or query parameter",
        messageFormat: "Parameter '{0}' on method '{1}' has type '{2}', which is not trivially convertible to/from a URL segment or query string. Route/query parameters should be primitives, string, enum, Guid, DateTime, DateTimeOffset, or similar simple types; use [Body] for complex objects",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Route and query-string parameters must serialize to a single URL segment or query value; complex object types don't have a well-defined string form.");

    public static readonly DiagnosticDescriptor InvalidInterfaceShape = new(
        id: "RG0007",
        title: "Interface decorated with [ApiRoute] has an unsupported member",
        messageFormat: "Member '{0}' on interface '{1}' is not a method, or does not return Task or Task<T>. Every member of an [ApiRoute] interface must be a method returning Task or Task<T>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RouteGen interfaces model HTTP operations, one per method, each returning Task or Task<T>.");

    public static readonly DiagnosticDescriptor MissingHttpMethodAttribute = new(
        id: "RG0008",
        title: "Method has no HTTP verb attribute",
        messageFormat: "Method '{0}' on interface '{1}' has no [Get]/[Post]/[Put]/[Delete]/[Patch] attribute",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every method on an [ApiRoute] interface must declare exactly one HTTP verb attribute.");

    public static readonly DiagnosticDescriptor AmbiguousRazorPageName = new(
        id: "RG0101",
        title: "Ambiguous generated Paths member name",
        messageFormat: "Two @page routes ('{0}' in '{1}' and '{0}' in '{2}') would generate the same Paths member name '{3}'. Add [GeneratedPathName(\"...\")] to disambiguate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Paths generator derives member names from .razor file names; two components with the same file name (in different folders) collide unless disambiguated.");
}
