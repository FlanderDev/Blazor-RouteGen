using System.Collections.Generic;
using System.Text;

namespace RouteGen.Generators;

/// <summary>A single {name} or {name:constraint} or {name?} token found in a route template.</summary>
internal readonly struct RouteToken
{
    public string Name { get; }
    public string? Constraint { get; }
    public bool Optional { get; }

    public RouteToken(string name, string? constraint, bool optional)
    {
        Name = name;
        Constraint = constraint;
        Optional = optional;
    }
}

internal static class RouteTemplateParser
{
    /// <summary>
    /// Extracts every {token}/{token:constraint}/{token?} from a route template. Literal braces
    /// are not supported (ASP.NET Core route templates use "{{" / "}}" for literal braces, which
    /// this simple parser passes over as separate token boundaries -- acceptable for the
    /// route/URL-constant use case this package targets).
    /// </summary>
    public static IReadOnlyList<RouteToken> ExtractTokens(string template)
    {
        var tokens = new List<RouteToken>();
        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close < 0) break; // malformed; caller may choose to report a diagnostic separately
                string inner = template.Substring(i + 1, close - i - 1);
                i = close + 1;

                if (inner.Length == 0) continue;

                bool optional = inner.EndsWith("?");
                if (optional) inner = inner.Substring(0, inner.Length - 1);

                string? constraint = null;
                int colon = inner.IndexOf(':');
                string name = inner;
                if (colon >= 0)
                {
                    name = inner.Substring(0, colon);
                    constraint = inner.Substring(colon + 1);
                }

                // Strip a trailing default-value clause, e.g. {page=1}
                int eq = name.IndexOf('=');
                if (eq >= 0) name = name.Substring(0, eq);

                if (name.Length > 0)
                    tokens.Add(new RouteToken(name, constraint, optional));
            }
            else
            {
                i++;
            }
        }
        return tokens;
    }

    /// <summary>Combines a base route and a suffix into a single template, normalizing slashes.</summary>
    public static string Combine(string baseRoute, string? suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return baseRoute;
        var sb = new StringBuilder(baseRoute.TrimEnd('/'));
        sb.Append('/');
        sb.Append(suffix!.TrimStart('/'));
        return sb.ToString();
    }
}
