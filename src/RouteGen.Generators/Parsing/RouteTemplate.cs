using System.Collections.Generic;
using System.Text;

namespace RouteGen.Generators.Parsing;

/// <summary>
/// Minimal parser for ASP.NET Core-style route templates: splits a template into literal
/// segments and <c>{name}</c> / <c>{name:constraint}</c> / <c>{name:constraint?}</c> tokens.
/// This intentionally only supports the subset RouteGen needs (single-segment tokens, no
/// catch-all "*" params, no complex/cross-segment tokens) — good enough for the API-route and
/// Blazor-page-route scenarios described in the brief.
/// </summary>
internal static class RouteTemplate
{
    /// <summary>Extracts the ordered list of token names (without constraint or '?') from a route template.</summary>
    public static IReadOnlyList<RouteToken> ParseTokens(string template)
    {
        var tokens = new List<RouteToken>();
        if (string.IsNullOrEmpty(template))
        {
            return tokens;
        }

        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    break; // malformed; caller can separately validate if desired
                }

                var inner = template.Substring(i + 1, close - i - 1);
                var colonIndex = inner.IndexOf(':');
                var name = colonIndex >= 0 ? inner.Substring(0, colonIndex) : inner;
                var constraint = colonIndex >= 0 ? inner.Substring(colonIndex + 1) : null;
                var optional = false;

                if (name.EndsWith("?"))
                {
                    optional = true;
                    name = name.Substring(0, name.Length - 1);
                }
                else if (constraint != null && constraint.EndsWith("?"))
                {
                    optional = true;
                    constraint = constraint.Substring(0, constraint.Length - 1);
                }

                tokens.Add(new RouteToken(name, constraint, optional));
                i = close + 1;
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }

    /// <summary>Joins a base template and a suffix template with exactly one '/' between them.</summary>
    public static string Combine(string baseTemplate, string? suffix)
    {
        baseTemplate = baseTemplate.Trim('/');
        if (string.IsNullOrEmpty(suffix))
        {
            return baseTemplate;
        }

        suffix = suffix!.Trim('/');
        var sb = new StringBuilder(baseTemplate.Length + suffix.Length + 1);
        sb.Append(baseTemplate);
        if (sb.Length > 0 && suffix.Length > 0)
        {
            sb.Append('/');
        }

        sb.Append(suffix);
        return sb.ToString();
    }
}

internal readonly record struct RouteToken(string Name, string? Constraint, bool Optional);
