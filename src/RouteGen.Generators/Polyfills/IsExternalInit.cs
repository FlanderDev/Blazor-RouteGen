#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill required to use C# 9+ records and init-only properties when targeting
    /// netstandard2.0 — the real type only ships in .NET 5+ runtimes. The compiler only checks
    /// for the type's existence by name; this empty marker is all it needs. Roslyn analyzer
    /// projects (like RouteGen.Generators) must target netstandard2.0 for compatibility with
    /// all supported host compiler versions, but we still want records for the value-equatable
    /// incremental-generator model types, hence the polyfill.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
