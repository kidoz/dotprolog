namespace DotProlog.Packages;

internal sealed record PackageSource
{
    public string Version { get; set; } = "*";
    public string? Path { get; set; }
    public string? Git { get; set; }
    public string? Revision { get; set; }
    public string? Archive { get; set; }
    public string? Sha256 { get; set; }
}
