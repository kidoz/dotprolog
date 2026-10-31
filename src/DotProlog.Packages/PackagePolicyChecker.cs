using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotProlog.Packages;

/// <summary>Applies a root policy to packages whose bytes already match the lock.</summary>
internal static partial class PackagePolicyChecker
{
    public static bool Exists(string root) => File.Exists(Path.Combine(root, "propack.policy.json"));

    public static void Enforce(string root, IReadOnlyList<PolicyPackage> packages)
    {
        var policyPath = Path.Combine(root, "propack.policy.json");
        if (!File.Exists(policyPath))
            return;
        var policy =
            JsonSerializer.Deserialize(PackageFiles.ReadText(policyPath), PackageJsonContext.Default.PackagePolicy)
            ?? throw new InvalidDataException("Empty propack.policy.json.");
        if (policy.Schema != 1)
            throw new InvalidDataException("Unsupported policy schema.");
        List<PackageAdvisory> advisories = [.. policy.Advisories];
        foreach (var relative in policy.AdvisoryFiles)
        {
            var file = PackageFiles.Inside(root, relative);
            var feed =
                JsonSerializer.Deserialize(PackageFiles.ReadText(file), PackageJsonContext.Default.PackageAdvisoryFile)
                ?? throw new InvalidDataException($"Empty advisory file: {relative}");
            if (feed.Schema != 1)
                throw new InvalidDataException($"Unsupported advisory file schema: {relative}");
            advisories.AddRange(feed.Advisories);
        }
        var loaded = Validate(advisories, policy.Ignore);
        List<string> failures = [];
        foreach (var package in packages)
        {
            var hash = package.Hash;
            foreach (var advisory in loaded.Values)
            {
                if (!string.Equals(advisory.Package, package.Name, StringComparison.Ordinal))
                    continue;
                if (advisory.Version is not null && advisory.Version != package.Version)
                    continue;
                if (advisory.Hash is not null)
                {
                    hash ??= PackageFiles.HashTree(package.Directory);
                    if (!string.Equals(advisory.Hash, hash, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                failures.Add($"Advisory {advisory.Id} matches {package.Name} {package.Version}: {advisory.Summary}");
            }
            if (policy.Licenses is not null)
                CheckLicense(package, policy.Licenses, failures);
        }
        if (failures.Count > 0)
            throw new InvalidDataException(string.Join('\n', failures));
    }

    private static Dictionary<string, PackageAdvisory> Validate(List<PackageAdvisory> advisories, List<string> ignore)
    {
        Dictionary<string, PackageAdvisory> loaded = new(StringComparer.Ordinal);
        foreach (var advisory in advisories)
        {
            if (advisory is null || !AdvisoryId().IsMatch(advisory.Id))
                throw new InvalidDataException(
                    "An advisory id must start with a letter or digit and use only letters, digits, '.', '_' or '-'."
                );
            if (!loaded.TryAdd(advisory.Id, advisory))
                throw new InvalidDataException($"Duplicate advisory id {advisory.Id}.");
            PackageWorkspace.ValidateName(advisory.Package);
            if (string.IsNullOrWhiteSpace(advisory.Summary))
                throw new InvalidDataException($"Advisory {advisory.Id} requires a summary.");
            if (advisory.Version is null && advisory.Hash is null)
                throw new InvalidDataException($"Advisory {advisory.Id} requires a version or a tree hash.");
            if (advisory.Version is not null)
                PackageVersions.Parse(advisory.Version);
            if (advisory.Hash is not null && (advisory.Hash.Length != 64 || advisory.Hash.Any(c => !Uri.IsHexDigit(c))))
                throw new InvalidDataException($"Advisory {advisory.Id} hash must contain exactly 64 hexadecimal digits.");
        }
        HashSet<string> ignored = new(StringComparer.Ordinal);
        foreach (var id in ignore)
        {
            if (!ignored.Add(id))
                throw new InvalidDataException($"Advisory {id} is ignored more than once.");
            if (!loaded.Remove(id))
                throw new InvalidDataException($"Ignored advisory {id} is not listed.");
        }
        return loaded;
    }

    private static void CheckLicense(PolicyPackage package, List<string> allowed, List<string> failures)
    {
        var path = Path.Combine(package.Directory, "UPSTREAM.json");
        if (!File.Exists(path))
        {
            failures.Add($"Package {package.Name} has no UPSTREAM.json license declaration.");
            return;
        }
        using var document = JsonDocument.Parse(PackageFiles.ReadText(path));
        if (
            !document.RootElement.TryGetProperty("license", out var license)
            || license.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(license.GetString())
        )
        {
            failures.Add($"Package {package.Name} has no UPSTREAM.json license declaration.");
            return;
        }
        var expression = license.GetString()!;
        try
        {
            if (!PackageLicenseExpression.Allows(expression, allowed.ToHashSet(StringComparer.Ordinal)))
                failures.Add($"Package {package.Name} license '{expression}' is not allowed.");
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package {package.Name} {exception.Message}");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AdvisoryId();
}
