namespace DotProlog.Tool.Tests;

public sealed class ToolCommandTests : IDisposable
{
    [Theory]
    [InlineData("lint")]
    [InlineData("run")]
    public void DeepSourceReturnsADiagnostic(string command)
    {
        var path = Source("deep.pl", new string('(', 20_000) + "a" + new string(')', 20_000) + ".");
        (var exitCode, _, var error) = Execute(command, path);
        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL0012", error, StringComparison.Ordinal);
        Assert.Contains(path + "(1,", error, StringComparison.Ordinal);
    }

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
    public void LintReportsLiteralSemanticWarningsThroughTheCli()
    {
        var path = Source("format.pl", "run :- format(\"~w\", [a, b]).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3013", error, StringComparison.Ordinal);
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
    public void LintResolvesNamedFilesAsOneProgram()
    {
        var main = Source("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go :- helper.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void LintReportsAnUndefinedCrossFileCall()
    {
        var main = Source("main.pl", ":- module(main, []). :- use_module('other.pl'). go :- missing.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3029", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAGroundCallWithNoMatchingClauseHead()
    {
        var path = Source("trivial.pl", "p(a). go :- p(b).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3033", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnreferencedPredicate()
    {
        var path = Source("reachability.pl", ":- module(m, [go/0]).\ngo.\nunused.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3034", error, StringComparison.Ordinal);
        Assert.Contains("m:unused/0", error, StringComparison.Ordinal);
        Assert.Contains("may be unreferenced", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnreferencedNonterminal()
    {
        var path = Source("grammar.pl", ":- module(m, []).\ndead_grammar --> [a].\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3036", error, StringComparison.Ordinal);
        Assert.Contains("m:dead_grammar//0", error, StringComparison.Ordinal);
        Assert.Contains("may be unreferenced", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnusedImport()
    {
        var main = Source("main.pl", ":- module(main, [go/0]). :- use_module('other.pl', [helper/0]). go.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3035", error, StringComparison.Ordinal);
        Assert.Contains("helper/0", error, StringComparison.Ordinal);
        Assert.Contains("may be unused", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAnUnresolvedInclude()
    {
        var main = Source("main.pl", ":- include('missing.pl').\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3032", error, StringComparison.Ordinal);
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

    [Fact]
    public void NonSteadfastRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("max.pl", "max(X, Y, X) :- X >= Y, !.\nmax(_, Y, Y).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "DPL3037", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3037", enabledError, StringComparison.Ordinal);
        Assert.Contains("max/3", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatingEnableRuleDoesNotDuplicateDiagnostics()
    {
        var path = Source("max.pl", "max(X, Y, X) :- X >= Y, !.\n");

        (var exitCode, _, var error) = Execute(
            "lint",
            "--enable-rule",
            "DPL3037",
            "--enable-rule",
            "dpl3037",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(1, exitCode);
        Assert.Equal(1, error.Split("DPL3037", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void NonTailRecursionRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("recursive.pl", "p(N) :- N > 0, p(N), writeln(N).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3038", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3038", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/1", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleOptInRulesCanBeEnabledTogether()
    {
        var path = Source("heuristics.pl", "max(X, Y, X) :- X >= Y, !.\np(N) :- N > 0, p(N), writeln(N).\n");

        (var exitCode, _, var error) = Execute(
            "lint",
            "--enable-rule",
            "DPL3037",
            "--enable-rule",
            "DPL3038",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3037", error, StringComparison.Ordinal);
        Assert.Contains("DPL3038", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "unknown", "unknown lint profile")]
    [InlineData("--enable-rule", "DPL3001", "unsupported lint rule")]
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
    [InlineData("--enable-rule", "missing diagnostic identifier")]
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

    [Theory]
    [InlineData("main :- writeln(hello).", 0, "first\nhello\n", "")]
    [InlineData("main :- fail.", 1, "first\n", "ERROR: main: false")]
    [InlineData("main :- X is foo + 1, writeln(X).", 2, "first\n", "ERROR: Arithmetic: `foo/0' is not a function")]
    [InlineData("main :- halt(3).", 3, "first\n", "")]
    public void RunEndsWithTheInitializationMainGoal(string main, int expectedExit, string expectedOutput, string expectedError)
    {
        var path = Source("main.pl", $":- initialization(main, main).\n:- initialization(writeln(first)).\n{main}\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(expectedExit, exitCode);
        Assert.Equal(expectedOutput, output.ReplaceLineEndings("\n"));
        Assert.Equal(expectedError, error.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void RunRunsTheGoalsOfAFileConsultedAtRunTime()
    {
        var inner = Source("inner.pl", ":- writeln(inner_directive).\n:- initialization(writeln(inner_init)).\n");
        var outer = Source(
            "outer.pl",
            $":- initialization(main).\n:- initialization(writeln(second)).\nmain :- consult('{inner.Replace('\\', '/')}'), writeln(done).\n"
        );

        (var exitCode, var output, var error) = Execute("run", outer);

        Assert.Equal(0, exitCode);
        Assert.Equal("done\ninner_directive\ninner_init\nsecond\n", output.ReplaceLineEndings("\n"));
        Assert.Empty(error);
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
