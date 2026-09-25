namespace DotProlog.Tool.Tests;

public sealed class ToolCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog.Tool.Tests", Guid.NewGuid().ToString("N"));

    public ToolCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LintReportsWarningsWithoutFailingByDefault()
    {
        var path = Source("singleton.pl", "value(X).\n");

        (var exitCode, var output, var error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{path}(1,7): warning DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsAsErrorsReturnsOne()
    {
        var path = Source("singleton.pl", "value(X).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderErrorsTakePrecedenceOverWarnings()
    {
        var path = Source("broken.pl", "value(X\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintAcceptsModesAndMultipleFiles()
    {
        var clean = Source("clean.pl", "same(X, X).\n");
        var warning = Source("warning.pl", "same(_Value, _Value).\n");

        (var exitCode, _, var error) = Execute("lint", "--mode", "modern", clean, warning);

        Assert.Equal(0, exitCode);
        Assert.Contains("DPL3002", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintDoesNotExecuteDirectives()
    {
        var path = Source("directive.pl", ":- initialization(halt(9)).\nsafe.\n");

        (var exitCode, var output, var error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public void SemanticProfileDoesNotImposeLayoutRules()
    {
        var path = Source("compact.pl", "pair(a,b).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void CovingtonProfileReportsLayoutWarnings()
    {
        var path = Source("compact.pl", "pair(a,b).\n");

        (var exitCode, _, var error) = Execute("lint", "--profile", "covington", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains($"{path}(1,7): warning DPL3007", error, StringComparison.Ordinal);
    }

    [Fact]
    public void IndividualThresholdEnablesItsLayoutCheck()
    {
        var path = Source("wide.pl", "long_name.\n");

        (var exitCode, _, var error) = Execute("lint", "--max-line-length", "5", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3005", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "unknown", "unknown lint profile")]
    [InlineData("--indent-size", "0", "invalid value")]
    [InlineData("--max-line-length", "word", "invalid value")]
    [InlineData("--max-clause-lines", "-1", "invalid value")]
    public void InvalidLintOptionValuesAreUsageErrors(string option, string value, string message)
    {
        var path = Source("clean.pl", "clean.\n");

        (var exitCode, _, var error) = Execute("lint", option, value, path);

        Assert.Equal(64, exitCode);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "missing lint profile")]
    [InlineData("--indent-size", "missing positive integer")]
    [InlineData("--max-line-length", "missing positive integer")]
    [InlineData("--max-clause-lines", "missing positive integer")]
    public void MissingLintOptionValuesAreUsageErrors(string option, string message)
    {
        (var exitCode, _, var error) = Execute("lint", option);

        Assert.Equal(64, exitCode);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWritesProgramOutputToTheProvidedStream()
    {
        var path = Source("run.pl", ":- initialization(writeln(ok)).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunReportsAnErrorADirectiveRaisesWhileLoading()
    {
        var path = Source("directive-error.pl", ":- nowhere(1).\n:- initialization(writeln(never)).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(70, exitCode);
        Assert.Empty(output);
        Assert.Equal("error: existence_error(procedure, nowhere/1)\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MissingLintInputIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("lint");

        Assert.Equal(64, exitCode);
        Assert.Contains("Usage: dotnet prolog lint", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLintModeIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("lint", "--mode");

        Assert.Equal(64, exitCode);
        Assert.Contains("missing language mode", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunHonorsAFlagOverride()
    {
        var path = Source("flag.pl", ":- initialization((current_prolog_flag(double_quotes, atom), writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", "--flag", "double_quotes=atom", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunRejectsAnInvalidFlagOverride()
    {
        var path = Source("flag.pl", "value.\n");

        (var exitCode, _, var error) = Execute("run", "--flag", "double_quotes=strings", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("invalid flag override", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunRejectsARepeatedFlagOverride()
    {
        var path = Source("flag.pl", "value.\n");

        (var exitCode, _, var error) = Execute("run", "--flag", "double_quotes=atom", "--flag", "double_quotes=chars", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRunFlagValueIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("run", "--flag");

        Assert.Equal(64, exitCode);
        Assert.Contains("missing flag override", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintAcceptsAFlagOverride()
    {
        var path = Source("clean.pl", "same(X, X).\n");

        (var exitCode, _, var error) = Execute("lint", "--flag", "double_quotes=codes", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void LintJudgesDoubleQuotedTextUnderTheSelectedReading()
    {
        var path = Source("convert.pl", "digits(N) :- number_codes(N, \"42\").\n");

        (var charsExit, _, var charsError) = Execute("lint", "--warnings-as-errors", path);
        (var codesExit, _, var codesError) = Execute("lint", "--warnings-as-errors", "--flag", "double_quotes=codes", path);
        (var strictExit, _, var strictError) = Execute("lint", "--warnings-as-errors", "--mode", "strict-iso", path);

        Assert.Equal(1, charsExit);
        Assert.Contains($"{path}(1,30): warning DPL3011", charsError, StringComparison.Ordinal);
        Assert.Equal(0, codesExit);
        Assert.Empty(codesError);
        Assert.Equal(0, strictExit);
        Assert.Empty(strictError);
    }

    [Fact]
    public void RunDefaultsToModernMode()
    {
        var path = Source("default.pl", ":- initialization((\"ab\" == [a,b], writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunReadsCodeListsUnderACodesOverride()
    {
        var path = Source("codes.pl", ":- initialization((\"ab\" == [97,98], writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", "--flag", "double_quotes=codes", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunRejectsTheRemovedExtendedMode()
    {
        var path = Source("clean.pl", "same(X, X).\n");

        (var exitCode, _, var error) = Execute("run", "--mode", "extended", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("unknown language mode: extended", error, StringComparison.Ordinal);
        Assert.Contains("modern|strict-iso", error, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Source(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static (int ExitCode, string Output, string Error) Execute(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = Program.Execute(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }
}
