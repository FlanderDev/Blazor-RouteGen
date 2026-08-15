namespace Sample.Shared;

public sealed class Mod
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

public sealed class ModListResult
{
    public IReadOnlyList<Mod> Items { get; set; } = Array.Empty<Mod>();
    public int TotalCount { get; set; }
}

public sealed class ModUploadDto
{
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}
