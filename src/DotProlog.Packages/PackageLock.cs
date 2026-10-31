namespace DotProlog.Packages;

internal sealed record PackageLock
{
    public int Schema { get; set; } = 1;
    public string ManifestHash { get; set; } = "";
    public string Engine { get; set; } = "";
    public List<LockedPackage> Packages { get; set; } = [];
}
