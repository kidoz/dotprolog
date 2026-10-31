namespace DotProlog.Packages;

internal sealed record PackageAdvisory
{
    public string Id { get; set; } = "";
    public string Package { get; set; } = "";
    public string? Version { get; set; }
    public string? Hash { get; set; }
    public string Summary { get; set; } = "";
}
