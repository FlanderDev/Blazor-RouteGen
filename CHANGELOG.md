# Changelog

All notable changes to this project are documented here. Versions correspond to the `RouteGen`
and `RouteGen.Abstractions` NuGet package versions, which are released together (see
`.github/workflows/release.yml` — pushing a `vX.Y.Z` tag builds, tests, and publishes both).

## [Unreleased]

### Added
- `ApiSurfaceGenerator`: emits an abstract MVC controller base and/or an `IHttpClientFactory`
  client implementation from a single `[ApiRoute]`-decorated interface.
- `PathsGenerator`: emits a static `Paths` class from `.razor` `@page` directives.
- Diagnostics `RG0001`–`RG0008` and `RG0101`.
- `RouteGen.Abstractions`: attribute vocabulary (`[ApiRoute]`, `[Get]`/`[Post]`/`[Put]`/`[Delete]`/`[Patch]`,
  `[Query]`, `[Body]`, `[Route]`, `[HttpClientName]`, `[GeneratedPathName]`) and `ApiException`.
- Sample Blazor WASM Hosted solution (`samples/`) demonstrating end-to-end usage.
- CI workflow (build + test + pack on every push/PR) and release workflow (tag-triggered
  GitHub Release with packages attached, optional NuGet.org publish).

[Unreleased]: https://github.com/your-org/RouteGen/compare/main...HEAD
