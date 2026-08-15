# RouteGen

Roslyn incremental source generators that eliminate hand-maintained route/URL constants in
Blazor WebAssembly **Hosted** + ASP.NET Core solutions. Declare an API surface **once**, as a
plain C# interface in your Shared project; RouteGen generates the real ASP.NET Core MVC
controller routing on the server and the real `HttpClient` calls on the client — from the same
declaration — so the two structurally cannot drift apart.

## The problem

A typical Blazor WASM Hosted solution is three projects: `App.Server` (ASP.NET Core Web API),
`App.Client` (Blazor WASM), `App.Shared` (DTOs + constants, referenced by both). Because the
client can't use `LinkGenerator`/`IUrlHelper` (server-only, request-scoped), teams hand-write a
constants class in Shared and *separately* declare the real route on the server controller.
Nothing checks the two agree. In practice: someone uses the wrong constant in a
`[Route]`/`[HttpGet]` attribute, ASP.NET Core silently combines it with the controller's base
route into something like `api/profile/api/profile/mods`, and the client's request 404s. This
compiles fine and fails silently at runtime.

RouteGen removes the second, hand-maintained description entirely. You write the interface;
the controller routing and the client's HTTP calls are both generated from it at compile time.

## Installation

```bash
dotnet add package RouteGen.Abstractions   # in Shared, Server, and Client
dotnet add package RouteGen                # in Server and Client (the generator itself)
```

`RouteGen.Abstractions` contains the attribute definitions and `ApiException` — it's a normal
library reference, needed everywhere the attributes or the exception type are used in source.
`RouteGen` is the analyzer package (source generators only); it only needs to go in the projects
that should get generated output (Server, Client), not Shared.

`RouteGen` is a `DevelopmentDependency`, so it won't flow transitively to anything that
references your Server or Client project — nothing to configure there.

## Quick start

### 1. Declare the contract once, in Shared

```csharp
[ApiRoute("api/mods", HttpClientName = "App")]
public partial interface IModsApi
{
    [Get]
    Task<ModListResult> GetMods([Query] int page = 1, [Query] int pageSize = 18, [Query] string? search = null);

    [Get("{id:int}")]
    Task<Mod> GetMod(int id);

    [Post("upload")]
    [Authorize]
    Task<Mod> Upload([Body] ModUploadDto dto);

    [Delete("{id:int}")]
    [Authorize(Roles = "Admin")]
    Task Delete(int id, CancellationToken ct = default);
}
```

### 2. Server: write a thin controller against the generated base class

RouteGen emits `ModsApiControllerBase` (an abstract `ControllerBase` with the real
`[Route]`/`[HttpGet]`/etc. already applied). You inherit it and override each action:

```csharp
public sealed class ModsController(IModsService service) : ModsApiControllerBase
{
    public override async Task<ActionResult<ModListResult>> GetMods(int page = 1, int pageSize = 18, string? search = null)
        => Ok(await service.GetMods(page, pageSize, search));

    public override async Task<ActionResult<Mod>> GetMod(int id)
        => await service.GetMod(id) is { } mod ? Ok(mod) : NotFound();

    // ...
}
```

You never write a route attribute or a route string. Registration needs nothing beyond normal
controller discovery: `builder.Services.AddControllers()` / `app.MapControllers()`. That's one
of the practical advantages of targeting controllers first — the future minimal-API emitter (see
"Roadmap" below) will need an explicit `app.Map...()` call per surface; controllers don't.

**Why the return type differs from the interface.** The interface says `Task<ModListResult>`;
the generated abstract method says `Task<ActionResult<ModListResult>>`. This is intentional, not
a bug: the point of parity between the interface and the generated base class is the *route,
parameters, and attributes*, not a literal interface implementation. `ActionResult<T>` is the
idiomatic MVC return shape and lets your override return `NotFound()`, `BadRequest()`,
`Forbid()`, etc., not just the happy path.

### 3. Client: register the generated implementation

RouteGen emits `HttpModsApi : IModsApi`, a concrete class that makes the real `HttpClient`
calls:

```csharp
builder.Services.AddHttpClient("App", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddScoped<IModsApi, HttpModsApi>();
```

Every Razor component now injects `IModsApi` and calls plain C# methods:

```csharp
@inject IModsApi ModsApi
...
var mods = await ModsApi.GetMods(search: "torch");
```

No `HttpClient`, no URL strings, anywhere in UI code.

### 4. Blazor page routes: the `Paths` class

Separately from the API generator, RouteGen scans every `.razor` file in the consuming project
for `@page "..."` directives and emits a static `Paths` class:

```csharp
public static class Paths
{
    public const string Mods = "/";
    public static string ModDetail(int id) => $"/mod/{id}";
}
```

```razor
<NavLink href="@Paths.ModDetail(mod.Id)">@mod.Name</NavLink>
```

**Member naming.** By default, the member name comes from the `.razor` file's own name
(`ModDetail.razor` → `ModDetail`), converted to PascalCase; the folder path is *not* included.
Two components with the same file name in different folders will collide — RouteGen reports
`RG0101` (warning) and keeps the first one (by route, alphabetically) rather than guessing.
Disambiguate with an explicit override:

```razor
@page "/admin/reports"
@attribute [GeneratedPathName("AdminReports")]
```

This is the part of RouteGen most likely to need a manual nudge in a real project with lots of
same-named `Index.razor` files — the override exists specifically for that.

## Attribute vocabulary

| Attribute | Target | Meaning |
|---|---|---|
| `[ApiRoute("api/mods", HttpClientName = "App")]` | interface | Base route for every method; which named `HttpClient` the generated client resolves via `IHttpClientFactory`. |
| `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]` | method | HTTP verb, with an optional route-template suffix appended to the interface's base route. |
| `[Query]` | parameter | Query-string parameter. Nullable/default parameters are omitted from the query string when null, rather than emitting `?x=`. |
| `[Body]` | parameter | The (at most one) parameter serialized as the JSON request body. Only valid on POST/PUT/PATCH. |
| `[Route("tokenName")]` | parameter | Explicit override: binds this parameter to a `{tokenName}` template token when the parameter's own name doesn't match it. Route parameters are otherwise inferred purely by name-matching against `{name}`/`{name:constraint}` tokens — no attribute needed in the common case. |
| `[HttpClientName("Auth")]` | method | Overrides the interface-level `HttpClientName` for a single method (e.g. a login endpoint outside the `api/*` base address). |
| `[Authorize]` / `[Authorize(Roles = "...")]` / `[AllowAnonymous]` | method | The real ASP.NET Core attributes (`Microsoft.AspNetCore.Authorization`). Copied verbatim onto the generated abstract controller method. |
| `[GeneratedPathName("Name")]` | `.razor` file, via `@attribute` | Overrides the inferred `Paths` member name for that page. |

A trailing `CancellationToken ct = default` parameter is recognized specially: excluded from the
URL and body, and flows through into the generated `HttpClient` call.

`Task` → no response body expected. `Task<T>` → deserializes the JSON response as `T`.
`Task<Stream>` → returns the raw response stream without JSON deserialization, for
file-download-style endpoints.

## Error handling

Generated client methods throw `RouteGen.Abstractions.ApiException` (carrying the HTTP method,
request URI, status code, and raw response body) on any non-success status code, instead of a
bare `HttpRequestException` from `EnsureSuccessStatusCode()`. Catch it like any other typed
exception:

```csharp
try { await ModsApi.Upload(dto); }
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { /* ... */ }
```

## Diagnostics

RouteGen's entire value proposition is catching at compile time what currently fails silently at
runtime, so these are real errors/warnings, not suggestions:

| ID | Severity | Meaning |
|---|---|---|
| `RG0001` | Error | Two methods on the same interface resolve to an identical route + HTTP verb. |
| `RG0002` | Error | `[Body]` used on a `[Get]`/`[Delete]` method (no request body on those verbs). |
| `RG0003` | Error | A `{token}` in the route template has no matching parameter. |
| `RG0004` | Error | A parameter isn't a route token, `[Query]`, or `[Body]` — nothing tells RouteGen what to do with it. |
| `RG0005` | Error | More than one parameter marked `[Body]`. |
| `RG0006` | Warning | A `[Query]`/route parameter's type isn't a primitive/string/enum/Guid/DateTime/etc.; it won't have a well-defined URL representation. |
| `RG0007` | Error | An `[ApiRoute]` interface member isn't a method returning `Task`/`Task<T>`. |
| `RG0008` | Error | A method has no `[Get]`/`[Post]`/`[Put]`/`[Delete]`/`[Patch]` attribute. |
| `RG0101` | Warning | Two `.razor` pages would generate the same `Paths` member name; see "Member naming" above. |

An interface with any **error**-level diagnostic doesn't get controller/client code generated
for it at all (rather than emitting something partially wrong around the error); warnings don't
block generation.

## How cross-project discovery works

Server and Client never declare the `[ApiRoute]` interface themselves — they only reference the
Shared project that does. A naive syntax-tree-based generator would never see it, since
`CreateSyntaxProvider` only sees declarations written in the *current* compilation's own source.
RouteGen instead walks the compilation's referenced-assembly symbol graph (see
`InterfaceDiscovery.cs`) looking for types carrying `RouteGen.Abstractions.ApiRouteAttribute`,
which works uniformly whether the interface was declared in this compilation or a referenced
one. This exact topology — Shared holds the interface, Server and Client are separate projects
each with their own generator invocation — is covered by an explicit test
(`Cross_Project_Boundary_Interface_Declared_In_Referenced_Assembly_Is_Discovered`), not assumed
to work because it usually does.

Each of Server and Client runs its own copy of the generator (that's how Roslyn analyzers work —
one invocation per compilation) and independently decides what to emit:

- References `Microsoft.AspNetCore.Mvc.ControllerBase`? → emits the abstract controller base.
- References `Microsoft.Extensions.Http.IHttpClientFactory`? → emits the client implementation.
- References both, or neither? → emits both, or neither (diagnostics still run either way).

## Known limitations

- **Performance on very large solutions.** Because interface discovery has to walk referenced
  assembly symbols on every compilation snapshot (not just source syntax), this pipeline is
  less incremental than a pure syntax-driven generator. `InterfaceDiscovery` skips assemblies
  whose name starts with `System`/`Microsoft.`/`netstandard`/`mscorlib` as a cheap guard, but an
  assembly named e.g. `Microsoft.MyCompany.Shared` would be skipped too — rename it, or adjust
  the guard, if you hit this.
- **Route templates are single-segment only.** No catch-all (`{*path}`) support, no
  cross-segment tokens. Fine for the REST-shaped routes this package targets; not a general
  ASP.NET Core routing-template implementer.
- **`Paths` member naming** is filename-based, not path-based, by design (see above) — expect to
  reach for `[GeneratedPathName]` on same-named files.
- No design-time tooling beyond the compiler diagnostics: no VS extension, no code-fix
  providers. (A code fix for the more mechanical diagnostics — e.g. adding a missing `[Query]` —
  would be a reasonable follow-up if there's appetite for it.)
- Reflection-based MVC controller/action discovery is a property of choosing **controllers** as
  the v1 target, not something RouteGen's own generated code does — see "Why controllers, and
  why not reflection" below.

## Why controllers, and why not reflection

The generator itself introduces **no runtime reflection**: routes, parameter bindings, and
attributes are all resolved at compile time into plain generated C#. What *is* reflection-based
is ASP.NET Core MVC's own controller/action discovery — an accepted characteristic of choosing
classic attribute-routed controllers as the v1 target, not something this package tries to work
around. Minimal API's request-delegate model is more source-generator-friendly and
trim/AOT-oriented, which is exactly why it's noted below as the natural v2 target — but
controllers are the more familiar, better-tooled, more debuggable starting point (mature
`[Authorize]`/`[Route]` inheritance behavior, no need to introduce an explicit
`app.Map...()`-per-surface registration step), so that's the deliberate choice for v1.

Two subtle-but-standard .NET/ASP.NET Core behaviors this relies on, and which are covered by
explicit tests rather than assumed:

- Attributes like `[Authorize]`/`[AllowAnonymous]` placed on an **abstract** method are picked
  up by a subclass that **overrides** that method without redeclaring them — standard
  inherited-attribute resolution on overridden virtual/abstract members (`AuthorizeAttribute`
  doesn't opt out of `Inherited = true`), which ASP.NET Core's action discovery uses directly.
- A class-level `[Route("...")]` on the abstract base is honored by a concrete subclass that
  declares no `[Route]` of its own — standard ASP.NET Core attribute-routing behavior for
  inherited class-level route attributes.

## Roadmap / explicitly out of scope for v1

- **Minimal API as a second server-side emission target** (`MapGroup`/`IEndpointRouteBuilder`),
  alongside — not replacing — the controller emitter. The shared attributed interface, the
  client generator, the `Paths` generator, and all diagnostics are designed to carry over
  unchanged; only the server-side emitter would be new.
- OpenAPI/Swagger generation or interop.
- Static-asset URL-prefix helpers (mentioned in the original problem statement as a possible
  future extension; not built).
- Code-fix providers for the diagnostics above (stretch goal, not required for v1).

## Repository layout

```
src/
  RouteGen.Abstractions/     Attributes + ApiException — normal library, reference everywhere
  RouteGen.Generators/       The two IIncrementalGenerators, packed as the "RouteGen" analyzer package
tests/
  RouteGen.Generators.Tests/ Snapshot-style generator tests (CSharpGeneratorDriver, no external testing framework needed)
samples/
  Sample.Shared/             The one hand-written IModsApi interface + DTOs
  Sample.Server/             Thin ModsController : ModsApiControllerBase, ASP.NET Core Web API
  Sample.Client/             Blazor WASM app consuming the generated HttpModsApi + Paths
global.json                  Pins the .NET SDK version used by local/CI/release builds
CHANGELOG.md                 Notable changes per released version
.github/workflows/ci.yml       Build + test + pack on every push/PR
.github/workflows/release.yml  Tag-triggered GitHub Release (+ optional NuGet.org publish)
```

Why one generator project instead of three separate NuGet packages for controller/client/paths:
they share the same parsing/model code (`Model/`, `Parsing/`) and are cheap to keep together;
splitting them wouldn't reduce what a consumer has to install (Server and Client both need "the
API generator" regardless of verb split) and would complicate the build. `PathsGenerator` is a
separate `IIncrementalGenerator` class within the same assembly/package because it operates on
an entirely different input (`AdditionalTexts`, not attributed symbols) and has nothing to share
with the API-surface pipeline beyond the route-template tokenizer.

## Releasing

Pushing a tag matching `vX.Y.Z` (optionally `vX.Y.Z-suffix` for prereleases, e.g. `v0.2.0-beta.1`)
triggers `.github/workflows/release.yml`, which:

1. Builds and runs the test suite.
2. Packs `RouteGen` and `RouteGen.Abstractions` with `Version` set from the tag (the two are
   versioned together — install them as a matching pair).
3. Builds the sample solution against the freshly-packed output, so a release can't ship if the
   packages don't actually work end to end.
4. Publishes a GitHub Release with both `.nupkg` files, a `SHA256SUMS.txt`, and a ready-to-open
   `RouteGen-Sample-X.Y.Z.zip` (the `samples/` solution plus the repo's `README`/`LICENSE`)
   attached.
5. If a `NUGET_API_KEY` repository secret is configured, also pushes both packages to
   NuGet.org (`--skip-duplicate`, so re-running a release is safe). Leave the secret unset to
   only publish to GitHub Releases.

```bash
git tag v0.2.0
git push origin v0.2.0
```

A `global.json` pins the SDK version (`8.0.400`, roll-forward `latestFeature`) so local builds,
CI, and release builds all use the same toolchain rather than "whatever's newest on the runner".

## Building locally

```bash
dotnet restore RouteGen.sln
dotnet build RouteGen.sln
dotnet test tests/RouteGen.Generators.Tests
dotnet pack src/RouteGen.Generators/RouteGen.Generators.csproj -o ./artifacts
dotnet pack src/RouteGen.Abstractions/RouteGen.Abstractions.csproj -o ./artifacts
```

The sample solution (`samples/`) references the generator via `ProjectReference` (as an
`Analyzer` item) rather than the packed NuGet package, so `dotnet build RouteGen.sln` works
without a prior `dotnet pack` + local NuGet feed. A real consumer installs the packed
`RouteGen`/`RouteGen.Abstractions` packages instead, per "Installation" above.

> **Note on this repository's provenance:** this repo was generated in an environment without a
> .NET SDK or internet access to install one, so the code has **not** been compiled or run here.
> The design follows established, well-documented Roslyn incremental-generator and ASP.NET Core
> patterns throughout, but treat first `dotnet build` as the real first compile — see
> "First build checklist" below for the likeliest rough edges.

## First build checklist

Things most likely to need a small fix on the first real `dotnet build`, given the above:

1. **Package versions.** `Microsoft.CodeAnalysis.CSharp` (4.11.0), ASP.NET Core packages
   (8.0.8), xunit (2.9.2) — pin to whatever's current when you restore; these were current as of
   this writing but NuGet will simply fail to restore a yanked/superseded version rather than
   silently substituting one.
2. **`ClientEmitter`'s query-string building** uses `Uri.EscapeDataString(x.ToString()!)` for
   non-string query values — fine for primitives/enums/Guid/DateTime, but double-check the
   `DateTime`/`DateTimeOffset` default `ToString()` round-trips the way your server-side
   `[FromQuery]` model binder expects; you may want `ToString("O")` for those instead.
3. **`ApiSurfaceGenerator`'s per-interface error suppression** (skip emitting for an interface
   with any error diagnostic) matches diagnostics to interfaces by checking whether the
   diagnostic's message text contains the interface or method name, since `Diagnostic` doesn't
   carry arbitrary payload. This is intentionally coarse; if a method name is a substring of
   another identifier in a way that causes over-suppression in your codebase, tighten it to
   parse the ID/interface out of the message more precisely.
4. **`netstandard2.0` + C# 11 raw features**: the generator project sets `<LangVersion>latest</LangVersion>`
   on a `netstandard2.0` target, which is fine for compiler-feature syntax (records, required
   modern C# used in `Model/ApiModels.cs`) but worth confirming your installed SDK's default
   toolset agrees — pin `<LangVersion>` explicitly (e.g. `12.0`) if you hit a version mismatch.
5. **Sample.Client / Sample.Server** were written against the Blazor WASM Hosted + `net8.0`
   shape described in the brief; if you're on a different .NET version, bump the
   `<TargetFramework>` and package versions together across all six projects.
