namespace DotProlog.Packages;

internal readonly record struct PolicyPackage(string Name, string Version, string? Hash, string Directory);
