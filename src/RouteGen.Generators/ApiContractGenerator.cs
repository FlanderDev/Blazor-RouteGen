using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace RouteGen.Generators;

/// <summary>
/// Reads every <c>[ApiRoute]</c>-decorated interface visible to the current compilation (whether
/// declared in this project's own source or referenced from a shared project via a normal
/// project reference) and emits:
///   - an abstract MVC controller base class, when the compilation references
///     <c>Microsoft.AspNetCore.Mvc.ControllerBase</c> (i.e. this is the ASP.NET Core server
///     project), or
///   - a concrete <c>HttpClient</c>-backed implementation of the interface otherwise (i.e. this
///     is the Blazor WebAssembly client project, or any other C# client project).
///
/// Both emission paths, plus the collision/type diagnostics, run off the exact same parsed
/// <see cref="ApiInterfaceModel"/> so the server and client structurally cannot drift.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ApiContractGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaceDeclarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            "RouteGen.ApiRouteAttribute",
            predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        // Interfaces can be declared `partial` and split across files; dedupe by fully qualified
        // name so we don't process (and emit for) the same interface twice.
        var distinctInterfaces = interfaceDeclarations
            .Collect()
            .Select(static (symbols, _) => Dedupe(symbols));

        var isServerProject = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase") is not null);

        var combined = distinctInterfaces.Combine(isServerProject);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (interfaces, isServer) = pair;

            foreach (var interfaceSymbol in interfaces)
            {
                var diagnostics = new List<Diagnostic>();
                var model = ApiInterfaceReader.TryParse(interfaceSymbol, diagnostics);
                foreach (var d in diagnostics) spc.ReportDiagnostic(d);
                if (model is null || model.Methods.Count == 0) continue;

                string source = isServer
                    ? ServerControllerEmitter.Emit(model)
                    : ClientImplementationEmitter.Emit(model);

                string fileNameHint = (isServer ? "Server_" : "Client_") + model.InterfaceName + ".g.cs";
                spc.AddSource(fileNameHint, source);
            }
        });
    }

    private static List<INamedTypeSymbol> Dedupe(ImmutableArray<INamedTypeSymbol> symbols)
    {
        var seen = new HashSet<string>();
        var result = new List<INamedTypeSymbol>();
        foreach (var s in symbols)
        {
            string key = s.ToDisplayString();
            if (seen.Add(key)) result.Add(s);
        }
        return result;
    }
}
