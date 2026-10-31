namespace DotProlog.Packages.Tests;

[Collection("Git environment")]
public sealed class GitEnvironmentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "propack git " + Guid.NewGuid().ToString("N"));

    public GitEnvironmentTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => PackageFiles.DeleteDirectory(_directory);

    [Theory]
    [InlineData("global")]
    [InlineData("environment")]
    [InlineData("directory")]
    public void Acquisition_ignores_inherited_Git_configuration(string configuration)
    {
        var source = Path.Combine(_directory, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "propack.json"), "{\"name\":\"demo/lib\"}");
        File.WriteAllText(Path.Combine(source, ".gitattributes"), "data.inc filter=propack-test\n");
        File.WriteAllText(Path.Combine(source, "data.inc"), "original bytes\n");
        Git("init", "--quiet", source);
        Git("-C", source, "add", ".");
        Git(
            "-C",
            source,
            "-c",
            "user.name=Package Test",
            "-c",
            "user.email=package@example.invalid",
            "-c",
            "core.hooksPath=" + _directory,
            "commit",
            "--quiet",
            "-m",
            "Test package"
        );
        var config = Path.Combine(_directory, "config");
        File.WriteAllText(config, "[filter \"propack-test\"]\nsmudge = git hash-object --stdin\nrequired = true\n");
        Dictionary<string, string> overrides = configuration switch
        {
            "global" => new() { ["GIT_CONFIG_GLOBAL"] = config },
            "environment" => new()
            {
                ["GIT_CONFIG_COUNT"] = "1",
                ["GIT_CONFIG_KEY_0"] = "filter.propack-test.smudge",
                ["GIT_CONFIG_VALUE_0"] = "git hash-object --stdin",
            },
            _ => new() { ["GIT_DIR"] = Path.Combine(_directory, "missing"), ["GIT_WORK_TREE"] = _directory },
        };
        var previous = overrides.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in overrides)
                Environment.SetEnvironmentVariable(key, value);
            var acquired = PackageFiles.Acquire(
                new PackageSource { Git = source, Revision = "HEAD" },
                Path.Combine(_directory, "cache")
            );
            Assert.Equal("original bytes\n", File.ReadAllText(Path.Combine(acquired.Directory, "data.inc")));
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public void Invalid_resolved_commit_is_rejected_before_checkout()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("This fake Git executable is a POSIX shell script.");
            return;
        }
        var git = Path.Combine(_directory, "git");
        var checkout = Path.Combine(_directory, "checkout-ran");
        File.WriteAllText(
            git,
            "#!/bin/sh\nfor arg in \"$@\"; do\ncase \"$arg\" in\nrev-parse) echo invalid-commit; exit 0;;\ncheckout) touch \""
                + checkout
                + "\"; exit 0;;\nesac\ndone\nexit 0\n"
        );
        File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var previous = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", _directory + Path.PathSeparator + previous);
            var error = Assert.Throws<InvalidDataException>(() =>
                PackageFiles.Acquire(
                    new PackageSource { Git = Path.Combine(_directory, "source"), Revision = "HEAD" },
                    Path.Combine(_directory, "cache")
                )
            );
            Assert.Contains("commit id", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(checkout));
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_directory, "cache")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
        }
    }

    private static void Git(params string[] arguments) => PackageFiles.ProcessOutput("git", arguments);
}
