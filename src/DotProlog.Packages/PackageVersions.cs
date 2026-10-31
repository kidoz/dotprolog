using System.Globalization;

namespace DotProlog.Packages;

internal static class PackageVersions
{
    public static Version Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("A version cannot be empty.");
        var parts = value.Split('.');
        if (
            parts.Length != 3
            || parts.Any(p =>
                p.Length == 0
                || (p.Length > 1 && p[0] == '0')
                || !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            )
            || !Version.TryParse(value, out var version)
        )
            throw new InvalidDataException($"Expected stable major.minor.patch version, got '{value}'.");
        return version;
    }

    public static bool Matches(string actual, string requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            throw new InvalidDataException("A version requirement cannot be empty.");
        var version = Parse(actual);
        if (requirement == "*")
            return true;
        if (!requirement.StartsWith('^'))
            return version == Parse(requirement);
        var minimum = Parse(requirement[1..]);
        return version >= minimum
            && (
                minimum.Major > 0 ? version.Major == minimum.Major
                : minimum.Minor > 0 ? version.Major == 0 && version.Minor == minimum.Minor
                : version == minimum
            );
    }
}
