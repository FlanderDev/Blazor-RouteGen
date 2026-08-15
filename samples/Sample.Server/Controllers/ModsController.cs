using Microsoft.AspNetCore.Mvc;
using Sample.Server.Services;
using Sample.Shared;

namespace Sample.Server.Controllers;

// Inherits ModsApiControllerBase, which RouteGen generates from Sample.Shared.IModsApi. This
// class writes NO route attributes and NO route strings — routing, [Authorize] placement, and
// parameter binding all come from the generated base class, so they structurally cannot drift
// from what Sample.Client's generated HttpModsApi calls.
public sealed class ModsController(IModsService service) : ModsApiControllerBase
{
    public override async Task<ActionResult<ModListResult>> GetMods(int page = 1, int pageSize = 18, string? search = null)
        => Ok(await service.GetMods(page, pageSize, search));

    public override async Task<ActionResult<Mod>> GetMod(int id)
        => await service.GetMod(id) is { } mod ? Ok(mod) : NotFound();

    public override async Task<ActionResult<Mod>> Upload(ModUploadDto dto)
        => Ok(await service.Upload(dto));

    public override async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        => await service.Delete(id) ? NoContent() : NotFound();
}
