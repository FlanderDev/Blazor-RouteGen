using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using RouteGen.Generators.Emission;
using RouteGen.Generators.Model;
using RouteGen.Generators.Parsing;

namespace RouteGen.Generators;

/// <summary>
/// Reads every interface decorated with <c>[ApiRoute]</c> that's visible to the current
/// compilation (declared in this project or in any referenced project/assembly — see
/// <see cref="InterfaceDiscovery"/>), validates it, and emits:
///
/// <list type="bullet">
/// <item>an abstract MVC controller base class, if the compiling project references
/// <c>Microsoft.AspNetCore.Mvc.ControllerBase</c> (i.e. it's the Server project), and</item>
/// <item>a concrete <c>IHttpClientFactory</c>-based client implementation, if the compiling
/// project references <c>Microsoft.Extensions.Http.IHttpClientFactory</c> (i.e. it's the
/// Client project, or any other HttpClientFactory-based consumer).</item>
/// </list>
///
/// A project that references both (unusual, but not forbidden) gets both outputs. A project
/// that references neither (e.g. the Shared project itself) gets none — only diagnostics, so
/// mistakes are still caught wherever the interface is edited.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ApiSurfaceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pipeline = context.CompilationProvider.Select(static (compilation, ct) =>
        {
            var entries = ImmutableArray.CreateBuilder<InterfaceParseResult>();

            foreach (var interfaceSymbol in InterfaceDiscovery.FindApiRouteInterfaces(compilation))
            {
                ct.ThrowIfCancellationRequested();
                if (!ApiSurfaceParser.TryParse(interfaceSymbol, out var model, out var diags) || model is null)
                {
                    continue;
                }

                var hasError = diags.Any(d => d.Severity == DiagnosticSeverity.Error);
                entries.Add(new InterfaceParseResult(model, diags, hasError));
            }

            var hasControllerBase = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase") is not null;
            var hasHttpClientFactory = compilation.GetTypeByMetadataName("Microsoft.Extensions.Http.IHttpClientFactory") is not null;

            return new GeneratorResult(entries.ToImmutable(), hasControllerBase, hasHttpClientFactory);
        });

        context.RegisterSourceOutput(pipeline, static (spc, result) =>
        {
            foreach (var entry in result.Entries)
            {
                foreach (var diagnostic in entry.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                // Only emit code for an interface that parsed without any *error*-severity
                // diagnostic; warnings (e.g. RG0006 complex query type) don't block emission.
                if (entry.HasError)
                {
                    continue;
                }

                if (result.HasControllerBase)
                {
                    spc.AddSource($"{entry.Model.InterfaceName}.ControllerBase.g.cs", ControllerEmitter.Generate(entry.Model));
                }

                if (result.HasHttpClientFactory)
                {
                    spc.AddSource($"{entry.Model.InterfaceName}.Client.g.cs", ClientEmitter.Generate(entry.Model));
                }
            }
        });
    }

    private sealed record InterfaceParseResult(ApiInterfaceModel Model, ImmutableArray<Diagnostic> Diagnostics, bool HasError);

    private sealed record GeneratorResult(
        ImmutableArray<InterfaceParseResult> Entries,
        bool HasControllerBase,
        bool HasHttpClientFactory);
}
