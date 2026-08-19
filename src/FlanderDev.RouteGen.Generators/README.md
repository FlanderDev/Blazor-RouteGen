# FlanderDev.RouteGen.Generators

Roslyn source generators for [RouteGen](https://codeberg.org/FlanderDev/RouteGen).

This package generates:

- An abstract ASP.NET Core MVC controller base from your shared API interface
- A strongly typed `HttpClient` implementation for the Blazor client
- Strongly typed page-route helpers from `@page` directives in `.razor` files

## Quick start

```bash
# Shared project
dotnet add package FlanderDev.RouteGen.Abstractions

# Server + Client projects
dotnet add package FlanderDev.RouteGen.Abstractions
dotnet add package FlanderDev.RouteGen.Generators
```

Mark the generator package with `PrivateAssets="all"`:

```xml
<PackageReference Include="FlanderDev.RouteGen.Generators"
                  Version="0.2.4"
                  PrivateAssets="all" />
```

For page-route generation, also include your Razor files:

```xml
<AdditionalFiles Include="**/*.razor" />
```

Define the API once in the shared project, then:

- Inherit from the generated `*ControllerBase` on the server
- Inject the generated `Http*Api` (or the interface) on the client

See the [main README](https://github.com/FlanderDev/RouteGen) for full examples and diagnostics.

## License

MIT
