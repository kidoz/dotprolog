namespace Integration.Tests;

/// <summary>Locates the repository from a test binary, without baking in a build-time path.</summary>
internal static class RepositoryLayout
{
    /// <summary>The repository root, found by walking up to the directory holding the solution file.</summary>
    internal static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No DotProlog.slnx above {AppContext.BaseDirectory}.");
    }
}
