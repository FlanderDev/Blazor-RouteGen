using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace RouteGen.Generators.Parsing;

/// <summary>
/// Server and Client never declare <c>IModsApi</c>-style interfaces themselves — they only
/// reference the Shared project that does. A syntax-tree-based generator pipeline
/// (<c>CreateSyntaxProvider</c>) only sees declarations written in the *current* compilation's
/// own source, so it would never find them. Instead we walk the compilation's assembly symbol
/// graph (source module + every referenced assembly) looking for types carrying
/// <c>RouteGen.Abstractions.ApiRouteAttribute</c>, which works uniformly whether the interface
/// was declared in this compilation or a referenced one.
/// </summary>
internal static class InterfaceDiscovery
{
    public static IEnumerable<INamedTypeSymbol> FindApiRouteInterfaces(Compilation compilation)
    {
        var apiRouteAttribute = compilation.GetTypeByMetadataName("RouteGen.Abstractions.ApiRouteAttribute");
        if (apiRouteAttribute is null)
        {
            yield break; // Abstractions package not referenced (yet); nothing to do.
        }

        var seenAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var candidates = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
            {
                candidates.Add(asm);
            }
        }

        foreach (var assembly in candidates)
        {
            if (!seenAssemblies.Add(assembly))
            {
                continue;
            }

            // Cheap perf guard: skip BCL/framework assemblies, which can never contain an
            // [ApiRoute] interface but would otherwise dominate the walk on every keystroke.
            var name = assembly.Identity.Name;
            if (!SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                && (name.StartsWith("System") || name.StartsWith("Microsoft.") || name is "netstandard" or "mscorlib"))
            {
                continue;
            }

            foreach (var type in EnumerateNamespaceTypes(assembly.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Interface)
                {
                    continue;
                }

                foreach (var attr in type.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, apiRouteAttribute))
                    {
                        yield return type;
                        break;
                    }
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamespaceTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    foreach (var t in EnumerateNamespaceTypes(childNs))
                    {
                        yield return t;
                    }

                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in EnumerateNestedTypes(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNestedTypes(nested))
            {
                yield return deeper;
            }
        }
    }
}
