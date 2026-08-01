namespace DotProlog.Tool.Tests;

public sealed class ToolCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog.Tool.Tests", Guid.NewGuid().ToString("N"));

    public ToolCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LintReportsWarningsWithoutFailingByDefault()
    {
        string path = Source("singleton.pl", "value(X).\n");

        (int exitCode, string output, string error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{path}(1,7): warning DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsAsErrorsReturnsOne()
    {
        string path = Source("singleton.pl", "value(X).\n");

        (int exitCode, _, string error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderErrorsTakePrecedenceOverWarnings()
    {
        string path = Source("broken.pl", "value(X\n");

        (int exitCode, _, string error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintAcceptsModesAndMultipleFiles()
    {
        string clean = Source("clean.pl", "same(X, X).\n");
        string warning = Source("warning.pl", "same(_Value, _Value).\n");

        (int exitCode, _, string error) = Execute("lint", "--mode", "modern", clean, warning);

        Assert.Equal(0, exitCode);
        Assert.Contains("DPL3002", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintDoesNotExecuteDirectives()
    {
        string path = Source("directive.pl", ":- initialization(halt(9)).\nsafe.\n");

        (int exitCode, string output, string error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunWritesProgramOutputToTheProvidedStream()
    {
        string path = Source("run.pl", ":- initialization(writeln(ok)).\n");

        (int exitCode, string output, string error) = Execute("run", path);

        Assert.Equal(0, exitCode);
        Assert.Equal($"ok{Environment.NewLine}", output);
        Assert.Empty(error);
    }

    [Fact]
    public void MissingLintInputIsAUsageError()
    {
        (int exitCode, _, string error) = Execute("lint");

        Assert.Equal(64, exitCode);
        Assert.Contains("Usage: dotnet prolog lint", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLintModeIsAUsageError()
    {
        (int exitCode, _, string error) = Execute("lint", "--mode");

        Assert.Equal(64, exitCode);
        Assert.Contains("missing language mode", error, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Source(string name, string contents)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static (int ExitCode, string Output, string Error) Execute(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = Program.Execute(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }
}
