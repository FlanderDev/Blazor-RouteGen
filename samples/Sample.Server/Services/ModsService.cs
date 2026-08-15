using Sample.Shared;

namespace Sample.Server.Services;

public interface IModsService
{
    Task<ModListResult> GetMods(int page, int pageSize, string? search);
    Task<Mod?> GetMod(int id);
    Task<Mod> Upload(ModUploadDto dto);
    Task<bool> Delete(int id);
}

public sealed class InMemoryModsService : IModsService
{
    private readonly List<Mod> _mods = new()
    {
        new Mod { Id = 1, Name = "Better Torches", Author = "alice" },
        new Mod { Id = 2, Name = "Faster Mining", Author = "bob" },
        new Mod { Id = 3, Name = "HD Textures", Author = "carol" },
    };

    private int _nextId = 4;

    public Task<ModListResult> GetMods(int page, int pageSize, string? search)
    {
        var query = _mods.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new ModListResult { Items = items, TotalCount = _mods.Count });
    }

    public Task<Mod?> GetMod(int id) => Task.FromResult(_mods.FirstOrDefault(m => m.Id == id));

    public Task<Mod> Upload(ModUploadDto dto)
    {
        var mod = new Mod { Id = _nextId++, Name = dto.Name, Author = dto.Author };
        _mods.Add(mod);
        return Task.FromResult(mod);
    }

    public Task<bool> Delete(int id)
    {
        var mod = _mods.FirstOrDefault(m => m.Id == id);
        if (mod is null)
        {
            return Task.FromResult(false);
        }

        _mods.Remove(mod);
        return Task.FromResult(true);
    }
}
