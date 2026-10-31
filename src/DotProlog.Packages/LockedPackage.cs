namespace DotProlog.Packages;

internal sealed record LockedPackage
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public PackageSource Source { get; set; } = new();
    public string? RequestedRevision { get; set; }
    public string Hash { get; set; } = "";
}
