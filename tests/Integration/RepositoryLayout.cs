namespace Integration.Tests;

/// <summary>Locates the repository from a test binary, without baking in a build-time path.</summary>
internal static class RepositoryLayout
{
    /// <summary>The repository root, found by walking up to the directory holding the solution file.</summary>
    internal static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        string? fromBinary = FindAbove(AppContext.BaseDirectory);
        if (fromBinary is not null)
        {
            return fromBinary;
        }

        string? fromWorkingDirectory = FindAbove(Directory.GetCurrentDirectory());
        return fromWorkingDirectory
            ?? throw new InvalidOperationException(
                $"No DotProlog.slnx above {AppContext.BaseDirectory} or {Directory.GetCurrentDirectory()}."
            );
    }

    private static string? FindAbove(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
