namespace DotProlog.Packages;

internal sealed record PackageAdvisoryFile
{
    public int Schema { get; set; } = 1;
    public List<PackageAdvisory> Advisories { get; set; } = [];
}
