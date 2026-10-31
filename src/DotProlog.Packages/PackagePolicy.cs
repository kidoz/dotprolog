namespace DotProlog.Packages;

internal sealed record PackagePolicy
{
    public int Schema { get; set; } = 1;
    public List<PackageAdvisory> Advisories { get; set; } = [];
    public List<string> AdvisoryFiles { get; set; } = [];
    public List<string> Ignore { get; set; } = [];
    public List<string>? Licenses { get; set; }
}
