using Microsoft.AspNetCore.Authorization;
using RouteGen.Abstractions;

namespace Sample.Shared;

// This is the ONLY hand-written piece describing the "api/mods" surface. RouteGen generates,
// from this interface alone:
//   - Sample.Server:  an abstract ModsApiControllerBase with the real [Route]/[HttpGet]/etc.
//   - Sample.Client:  a concrete HttpModsApi : IModsApi that makes the real HttpClient calls.
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
