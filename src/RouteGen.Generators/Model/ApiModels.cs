using System.Collections.Immutable;

namespace RouteGen.Generators.Model;

/// <summary>
/// Immutable, value-equatable description of a single <c>[Query]</c>/<c>[Body]</c>/route
/// parameter on an interface method. Record equality drives IIncrementalGenerator caching, so
/// every field that affects generated output must be included and every field must itself be
/// value-equatable (hence <see cref="ImmutableArray{T}"/> rather than arrays/lists elsewhere).
/// </summary>
internal sealed record ApiParameterModel(
    string Name,
    string TypeDisplayString,
    bool IsNullable,
    bool HasDefaultValue,
    string? DefaultValueLiteral,
    ParameterKind Kind,
    string? RouteTokenName,
    bool IsCancellationToken,
    bool IsSimpleType);

internal enum ParameterKind
{
    Route,
    Query,
    Body,
    CancellationToken,
}

internal sealed record AttributeUsageModel(
    string AttributeTypeName,
    ImmutableArray<string> ConstructorArgumentLiterals,
    ImmutableArray<(string Name, string ValueLiteral)> NamedArguments)
{
    public bool Is(string simpleName) => AttributeTypeName == simpleName;
}

internal sealed record ApiMethodModel(
    string Name,
    string HttpVerb,
    string? RouteTemplateSuffix,
    string ResolvedRouteTemplate,
    string ReturnTypeDisplayString,
    ApiMethodReturnKind ReturnKind,
    string? UnwrappedReturnTypeDisplayString,
    ImmutableArray<ApiParameterModel> Parameters,
    ImmutableArray<AttributeUsageModel> AuthorizationAttributes,
    string? HttpClientNameOverride,
    Location MethodLocation);

internal enum ApiMethodReturnKind
{
    /// <summary>Task, no body.</summary>
    TaskVoid,
    /// <summary>Task&lt;T&gt;, JSON body.</summary>
    TaskOfJson,
    /// <summary>Task&lt;Stream&gt; or similar, raw binary body.</summary>
    TaskOfStream,
}

internal sealed record ApiInterfaceModel(
    string InterfaceName,
    string Namespace,
    string BaseRouteTemplate,
    string? HttpClientName,
    ImmutableArray<ApiMethodModel> Methods,
    Location InterfaceLocation);

/// <summary>
/// A lightweight, serializable location (file path + span) so models stay value-equatable
/// across compilations without holding onto Roslyn's non-equatable <c>Microsoft.CodeAnalysis.Location</c>.
/// </summary>
internal sealed record Location(string FilePath, int StartLine, int StartCharacter)
{
    public static readonly Location None = new(string.Empty, 0, 0);
}
