namespace DotProlog.Packages;

internal sealed record PackageManifest
{
    public int Schema { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.1.0";
    public string Engine { get; set; } = "dotprolog";
    public string? Catalog { get; set; }
    public Dictionary<string, string> Engines { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Modules { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, PackageSource> Dependencies { get; set; } = new(StringComparer.Ordinal);
}
