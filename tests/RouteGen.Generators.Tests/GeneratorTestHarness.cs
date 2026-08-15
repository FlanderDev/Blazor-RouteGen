using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RouteGen.Generators;

namespace RouteGen.Generators.Tests;

/// <summary>
/// Minimal, dependency-light generator test harness: builds a Compilation from one or more
/// source strings plus the real RouteGen.Abstractions attribute definitions (compiled inline,
/// so the test project doesn't need a package reference / restore of its own package), runs
/// <see cref="ApiSurfaceGenerator"/> and <see cref="PathsGenerator"/> via
/// <see cref="CSharpGeneratorDriver"/>, and hands back the generated trees + diagnostics.
/// </summary>
internal static class GeneratorTestHarness
{
    // Inlined rather than ProjectReference'd so a change to the shipping Attributes.cs is
    // exercised through the exact same text the real package ships — kept in sync by copying
    // the file's content; see AbstractionsSourceTests for a guard that catches drift.
    public const string AbstractionsSource = """
using System;

namespace RouteGen.Abstractions
{
    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class ApiRouteAttribute : Attribute
    {
        public ApiRouteAttribute(string template) => Template = template;
        public string Template { get; }
        public string? HttpClientName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class HttpClientNameAttribute : Attribute
    {
        public HttpClientNameAttribute(string name) => Name = name;
        public string Name { get; }
    }

    public abstract class HttpMethodAttribute : Attribute
    {
        protected HttpMethodAttribute(string verb, string? template) { Verb = verb; Template = template; }
        public string Verb { get; }
        public string? Template { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class GetAttribute : HttpMethodAttribute { public GetAttribute() : base("GET", null) { } public GetAttribute(string template) : base("GET", template) { } }
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class PostAttribute : HttpMethodAttribute { public PostAttribute() : base("POST", null) { } public PostAttribute(string template) : base("POST", template) { } }
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class PutAttribute : HttpMethodAttribute { public PutAttribute() : base("PUT", null) { } public PutAttribute(string template) : base("PUT", template) { } }
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class DeleteAttribute : HttpMethodAttribute { public DeleteAttribute() : base("DELETE", null) { } public DeleteAttribute(string template) : base("DELETE", template) { } }
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class PatchAttribute : HttpMethodAttribute { public PatchAttribute() : base("PATCH", null) { } public PatchAttribute(string template) : base("PATCH", template) { } }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class QueryAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class BodyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class RouteAttribute : Attribute { public RouteAttribute(string tokenName) => TokenName = tokenName; public string TokenName { get; } }
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GeneratedPathNameAttribute : Attribute { public GeneratedPathNameAttribute(string name) => Name = name; public string Name { get; } }
}
""";

    public static GeneratorRunResult Run(string source, bool includeAspNetCore, bool includeHttpClientFactory, IEnumerable<(string Path, string Content)>? additionalRazorFiles = null)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        };

        if (includeAspNetCore)
        {
            // Only referenced by tests that opt in — resolved from the shared framework via
            // FrameworkReference in the test csproj, present at test-runtime.
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Mvc.Core").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Mvc.Abstractions").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Authorization").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Http.Abstractions").Location));
        }

        if (includeHttpClientFactory)
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("Microsoft.Extensions.Http").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http.Json").Location));
        }

        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(AbstractionsSource, path: "Abstractions.cs"),
            CSharpSyntaxTree.ParseText(source, path: "Test.cs"),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "RouteGenTests",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var additionalTexts = (additionalRazorFiles ?? Enumerable.Empty<(string, string)>())
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create(
                new ApiSurfaceGenerator().AsSourceGenerator(),
                new PathsGenerator().AsSourceGenerator()),
            additionalTexts: additionalTexts);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        return new GeneratorRunResult(outputCompilation, runResult, generatorDiagnostics);
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;
        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }
}

internal sealed record GeneratorRunResult(
    Compilation OutputCompilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public string? GetGeneratedSource(string hintNameContains) =>
        RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains(hintNameContains))
            .SourceText?.ToString();
}
