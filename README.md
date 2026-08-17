# RouteGen

Roslyn incremental source generators that eliminate hand-maintained route/URL constants in
Blazor WebAssembly **Hosted** + ASP.NET Core solutions.

## The problem

The typical Blazor WebAssembly Hosted solution shape is three projects — `SampleApp.Server`
(ASP.NET Core Web API), `SampleApp.Client` (Blazor WebAssembly), `SampleApp.Shared` (DTOs + routes). Because
the client can't use `LinkGenerator`/`IUrlHelper` (server-only), teams hand-write a static
"constants" class in Shared and the server controller separately declares the *real* route.
Nothing checks the two agree. In practice this breaks exactly the way you'd expect: someone uses
the wrong constant in a `[Route]` attribute, ASP.NET Core silently combines it with the
controller-level route into something like `api/profile/api/profile/mods`, and the client's
request 404s. **This compiles fine and fails silently at runtime.**

RouteGen solves this at compile time: declare an API operation **once**, as a plain attributed
C# interface in the Shared project, and generate everything else from it.

```
Shared:  [ApiRoute] interface  ──┬──►  Server generator  ──►  abstract controller base (real [Route]/[HttpGet]/... attributes)
                                 └──►  Client generator   ──►  concrete HttpClient implementation
```

Because the generated controller base, the generated client, and the hand-written interface all
derive from the same attributed declaration — read via the semantic model, across the normal
project-reference boundary — there is exactly one source of truth and no way for client and
server to disagree about a route.

A second, independent generator does the same for Blazor page routes: it scans `@page`
directives in `.razor` files and emits a strongly-typed `Paths` class, so `NavLink`/`href` values
are never hand-typed either.

## Installation

```bash
dotnet add package FlanderDev.RouteGen.Abstractions
dotnet add package RouteGen --version 0.1.0
```

`FlanderDev.RouteGen.Abstractions` ships the attributes (`[ApiRoute]`, `[Get]`, `[Query]`, ...) and the
`ApiException` runtime type — reference it from your Shared, Server, and Client projects.
`RouteGen` is the analyzer package containing the generators themselves; add it (as
`PrivateAssets="all"`, which `dotnet add package` sets automatically for analyzer packages) to
whichever projects should actually emit generated code — normally Server and Client, **not**
Shared, since Shared only needs the attribute *definitions*, not the generator output.

For the page-route generator, also add your `.razor` files as `AdditionalFiles` in the Client
project:

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.razor" />
</ItemGroup>
```

## The attribute vocabulary

```csharp
// Shared project — the ONLY hand-written piece.
[ApiRoute("api/mods", HttpClientName = "App")]
public partial interface IModsApi
{
    [Get]
    Task<ModListResult> GetMods([Query] int page = 1, [Query] int pageSize = 18, [Query] string? search = null);

    [Get("{id:int}")]
    Task<ModDto> GetMod(int id);

    [Post("upload")]
    [Authorize]
    Task<ModDto> Upload([Body] ModUploadDto dto);

    [Delete("{id:int}")]
    [Authorize(Roles = "Admin")]
    Task Delete(int id, CancellationToken ct = default);
}
```

- **`[ApiRoute("api/mods")]`** — interface-level base route. `HttpClientName` selects which
  *named* `HttpClient` the generated client resolves via `IHttpClientFactory` (real solutions
  commonly need more than one base address, e.g. one for `/api/*` and a separate one for
  root-level auth endpoints). Defaults to `"Default"`.
- **`[Get]` / `[Post]` / `[Put]` / `[Delete]` / `[Patch]`** — each takes an optional route-template
  suffix string appended to the interface-level base route.
- Route parameters are **inferred by matching method-parameter names against `{name}`/
  `{name:constraint}` tokens** in the template — no separate `[Route]` needed per parameter. Use
  `[Route("tokenName")]` on a parameter as an explicit override when the parameter name and the
  token name must differ.
- **`[Query]`** marks a parameter as a query-string parameter. Nullable/optional query parameters
  are omitted from the generated client's query string when null, rather than emitting `?x=`.
- **`[Body]`** marks the (at most one) parameter serialized as the JSON request body.
- A trailing `CancellationToken` parameter is recognized specially — excluded from the URL and
  body, and flows through into the generated `HttpClient` call.
- **`[Authorize]` / `[Authorize(Roles = "...")]` / `[AllowAnonymous]`** on an interface method (or
  the whole interface) are propagated by the server generator onto the generated abstract
  controller's action methods.
- Return type conventions: `Task` → no response body expected (non-2xx still throws
  `ApiException`). `Task<T>` → deserialize the JSON response as `T`. `Task<Stream>` → raw
  binary/file-download style endpoint, not forced through JSON deserialization.

## Writing the server: the concrete controller

The generator emits an **abstract controller base** with real routing/binding/authorization
attributes already applied (`ModsApiControllerBase` for `IModsApi`). You write one thin,
ordinary controller against it:

```csharp
public sealed class ModsController(IModsService service) : ModsApiControllerBase
{
    public override async Task<ActionResult<ModListResult>> GetMods(int page, int pageSize, string? search)
        => Ok(await service.GetMods(page, pageSize, search));

    public override async Task<ActionResult<ModDto>> GetMod(int id)
        => await service.GetMod(id) is { } mod ? Ok(mod) : NotFound();
    // ...
}
```

You never write a route attribute or a route string by hand — routing entirely comes from the
generated base class. Note the generated base class's methods return `Task<ActionResult<T>>`
rather than literally `Task<T>` — that's deliberate, not a bug: it's the idiomatic MVC pattern
and lets your override return `NotFound()`, `BadRequest()`, `Forbid()`, etc., not just the happy
path. The point of parity between the interface and the generated base class is the *route,
parameters, and attributes*, not a literal interface implementation.

Registration needs nothing beyond normal ASP.NET Core controller discovery —
`builder.Services.AddControllers()` / `app.MapControllers()`. No extra generated registration
call is required, which is one practical advantage of targeting classic controllers for v1 (see
"Why controllers, not Minimal API" below).

## Using the client

The generator emits a concrete implementation (`HttpModsApi` for `IModsApi`) that you register
once:

```csharp
builder.Services.AddHttpClient("App", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddScoped<IModsApi, HttpModsApi>();
```

Every Razor component just injects `IModsApi` and calls plain C# methods — no `HttpClient`, no
URL strings, anywhere in UI code. Non-success responses throw `RouteGen.ApiException` (carrying
the `HttpStatusCode` and raw response body) instead of a bare `HttpRequestException`.

## Blazor page routes

Add `@attribute [GeneratedPathName("ModDetail")]` to a component only when the default naming
heuristic (derived from the `.razor` file's name) would be ambiguous or undesirable — e.g. two
different components that would otherwise both produce a member named `Detail`. Otherwise it
just works:

```razor
@page "/mod/{id:int}"
```

produces

```csharp
public static class Paths
{
    public const string Home = "/";
    public static string ModDetail(int id) => $"/mod/{id}";
}
```

in the `{RootNamespace}.Generated` namespace of the consuming project.

## Diagnostics

RouteGen's entire value proposition is catching at compile time what currently fails silently at
runtime, so diagnostics are not optional:

| Id | Severity | What it catches |
|----|----------|------------------|
| RG0001 | Error | Two methods on the same interface produce an identical route + HTTP verb. |
| RG0002 | Warning | `[Body]` used together with `[Get]`/`[Delete]`. |
| RG0003 | Error | A `{token}` in the route template has no matching parameter. |
| RG0004 | Error | A parameter doesn't match a route token and isn't `[Query]`/`[Body]`. |
| RG0005 | Error | More than one parameter marked `[Body]`. |
| RG0006 | Error | A route/query parameter's type isn't a primitive/string/enum/Guid/DateTime/etc. |
| RG0007 | Error | Two `@page` directives would generate the same `Paths` member name. |
| RG0008 | Error | A route template could not be parsed. |

## Sample

`samples/` contains a minimal, real, end-to-end Blazor WebAssembly Hosted app: `SampleApp.Shared`
holds `IModsApi`; `SampleApp.Server` has a thin `ModsController` over the generated abstract base;
`SampleApp.Client` has Razor pages calling the generated `HttpModsApi` and using the generated `Paths`
class from a `NavLink`. Run it with:

```bash
dotnet run --project samples/SampleApp.Server
```

After a build, inspect the generated files under
`samples/SampleApp.Server/obj/**/generated/FlanderDev.RouteGen.Generators/FlanderDev.RouteGen.Generators.ApiContractGenerator/`
and the equivalent path under `SampleApp.Client/obj/**` to see the emitted controller base and client
implementation.

## Why controllers, not Minimal API, for v1

Classic attribute-routed MVC controllers are the v1 server-side target. They're the more
familiar, more debuggable starting point, with well-trodden `[Authorize]`/`[Route]` inheritance
behavior (ASP.NET Core's attribute routing and controller/action discovery both correctly
resolve class- and method-level attributes declared on an *abstract base* and picked up by a
subclass that overrides without redeclaring them — verified as part of building this package,
since it's subtle enough to be worth confirming rather than assuming).

Minimal API's request-delegate model is more source-generator-friendly and trim/AOT-oriented,
which is exactly why it's the natural **v2** target — but it needs an explicit
`app.MapGroup(...)`/`IEndpointRouteBuilder` registration call that controllers don't. The same
shared-interface front end (client generator, page-route generator, diagnostics) is designed to
carry over unchanged when that emitter is added; only the server-side emission target changes.

## Non-functional characteristics

- Built as **incremental generators** (`IIncrementalGenerator`), not the legacy `ISourceGenerator`
  API, for IDE responsiveness in real projects.
- The generator assembly targets **netstandard2.0** (a hard Roslyn analyzer packaging
  requirement); consuming projects are expected to be on modern .NET (this repo's sample targets
  .NET 10, the current version at time of writing).
- No runtime reflection is introduced by RouteGen's own generated code — routes, parameter
  bindings, and attributes are resolved entirely at compile time into plain generated C#. This is
  separate from (and doesn't fight) the fact that ASP.NET Core MVC controllers themselves rely on
  the framework's own reflection-based controller/action discovery — an accepted characteristic
  of choosing controllers as the v1 target, not something this package tries to work around.
- Validated against the Shared/Server/Client project-reference topology as a first-class
  scenario: Shared holds the interface; Server and Client are separate projects, each with its
  own generator invocation reading the shared interface via the semantic model and emitting into
  its own compilation.
- Packaged as a correct Roslyn analyzer NuGet package: empty `lib/`, the generator DLL under
  `analyzers/dotnet/cs/`, and `DevelopmentDependency`/`PrivateAssets="all"` set so the generator
  doesn't leak into consumers' consumers. The attribute *definitions* and `ApiException` live in
  the separate `FlanderDev.RouteGen.Abstractions` package (referenced normally, not as an analyzer) since
  consumers need to see those types in source — bundling them only inside the analyzer package
  would make that impossible.

## Known limitations

- Literal `{{`/`}}` escaping in route templates (for literal braces) isn't specially handled by
  the simple template tokenizer used here — not a concern for the route/URL-constant use case
  this package targets, but worth knowing if your templates ever need literal braces.
- The page-route generator's member-naming heuristic is filename-based; use
  `[GeneratedPathName("...")]` to disambiguate when two components would otherwise collide (see
  RG0007).
- Static asset URL prefix generation is not implemented.

## Explicitly out of scope for v1 (future work)

- **Minimal API as a second server-side generation target** (`MapGroup`/`IEndpointRouteBuilder`),
  additive alongside the controller emitter, not a replacement for it.
- OpenAPI/Swagger generation or interop.
- Static-asset (`Assets`-style) URL helpers.
- Any design-time tooling beyond compiler diagnostics (no VS extension; a code-fix provider for
  the diagnostics above would be a reasonable stretch goal).

## Repository layout

```
src/
  FlanderDev.RouteGen.Abstractions/   attributes + ApiException (normal package reference)
  FlanderDev.RouteGen.Generators/     the incremental generators (analyzer package)
samples/
  SampleApp.Shared/              the one hand-written interface + DTOs
  SampleApp.Server/              thin controller over the generated abstract base
  SampleApp.Client/              Blazor WASM app calling the generated client + Paths
.github/workflows/
  release.yml              build+pack on every push/PR; on a v*.*.* tag, also
                            attaches the built .nupkg files to a GitHub Release
```

Generator unit tests are intentionally excluded from this drop. If adding them later,
golden-file/snapshot testing of generated source via `Microsoft.CodeAnalysis.CSharp.Testing` (or
a snapshot library) against representative `ApiInterfaceModel` inputs is the recommended
approach, plus an integration test asserting `[Authorize]` on an abstract base method is
correctly discovered by ASP.NET Core when a subclass overrides it without redeclaring the
attribute (see "Why controllers, not Minimal API" above).

## License

MIT — see [LICENSE](LICENSE).
