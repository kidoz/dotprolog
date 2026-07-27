using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary><c>format/1</c>, <c>format/2</c>, and <c>format/3</c>, directive by directive.</summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData("format(\"plain\")", "plain")]
    [InlineData("format(\"~w\", [foo(X)])", "foo(_G7)")]
    [InlineData("format(\"~w\", hello)", "hello")]
    [InlineData("format(\"~a\", ['a b'])", "a b")]
    [InlineData("format(\"~q\", ['a b'])", "'a b'")]
    [InlineData("format(\"~w and ~w\", [1, 2])", "1 and 2")]
    [InlineData("format(\"~d\", [42])", "42")]
    [InlineData("format(\"~2d\", [1234])", "12.34")]
    [InlineData("format(\"~2d\", [-1234])", "-12.34")]
    [InlineData("format(\"~2d\", [7])", "0.07")]
    [InlineData("format(\"~D\", [1234567])", "1,234,567")]
    [InlineData("format(\"~2f\", [3.14159])", "3.14")]
    [InlineData("format(\"~0f\", [3.7])", "4")]
    [InlineData("format(\"~e\", [1234.5])", "1.234500e+03")]
    [InlineData("format(\"~s\", [[104, 105]])", "hi")]
    [InlineData("format(\"~s\", [[h, i]])", "hi")]
    [InlineData("format(\"~c\", [0'x])", "x")]
    [InlineData("format(\"~3c\", [0'x])", "xxx")]
    [InlineData("format(\"~*c\", [3, 0'y])", "yyy")]
    [InlineData("format(\"~~\")", "~")]
    [InlineData("format(\"~i~w\", [skipped, kept])", "kept")]
    [InlineData("format(\"a~nb\")", "a\nb")]
    [InlineData("format(\"~2n\")", "\n\n")]
    public void WritesDirectives(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("format(\"[~w~t~10|]\", [left])", "[left     ]")]
    [InlineData("format(\"[~t~w~10|]\", [right])", "[    right]")]
    [InlineData("format(\"[~t~w~t~11|]\", [mid])", "[   mid    ]")]
    [InlineData("format(\"~`-t~10|\")", "----------")]
    [InlineData("format(\"~w~t~8|~w\", [name, value])", "name    value")]
    [InlineData("format(\"~w~t~4+~w\", [ab, cd])", "ab  cd")]
    [InlineData("format(\"~w~t~2|~w\", [toolong, x])", "toolongx")]
    // A column is counted from the start of the line, so the leading '[' occupies column 0 and
    // ~10| leaves ten characters before the ']'.
    public void AlignsColumns(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void ColumnStopsRestartOnEachLine() =>
        Assert.Equal(
            "a    1\nbb   2\n",
            PrologTestHost.RunGoal("forall(member(R-N, [a-1, bb-2]), format(\"~w~t~5|~w~n\", [R, N]))")
        );

    [Theory]
    [InlineData("format(atom(A), \"~w-~w\", [a, b]), write(A)", "a-b")]
    [InlineData("format(codes(C), \"hi\", []), write(C)", "[104,105]")]
    [InlineData("format(chars(C), \"hi\", []), write(C)", "[h,i]")]
    [InlineData("format(user_output, \"out\", [])", "out")]
    public void WritesToASink(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void TheFormatStringMayBeAnAtom() => Assert.Equal("hi", PrologTestHost.RunGoal("format('~w', [hi])"));

    [Fact]
    public void AnUnsupportedDirectiveIsReportedRatherThanWrittenThrough() =>
        Assert.Equal(
            "domain_error(format_directive,z)",
            PrologTestHost.RunGoal("catch(format(\"~z\", []), error(E, _), write(E))")
        );

    [Fact]
    public void RunningOutOfArgumentsIsReported() =>
        Assert.Equal(
            "domain_error(format_arguments,[only])",
            PrologTestHost.RunGoal("catch(format(\"~w~w\", [only]), error(E, _), write(E))")
        );

    [Fact]
    public void TabWritesSpaces() => Assert.Equal("a  b", PrologTestHost.RunGoal("write(a), tab(1 + 1), write(b)"));

    [Fact]
    public void FormatToAnUnknownStreamIsReported()
    {
        (RunResult result, string output, _) = PrologTestHost.Execute(
            ":- initialization(catch(format(nowhere, \"x\", []), error(E, _), write(E)))."
        );

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("domain_error(stream_or_alias,nowhere)", output);
    }
}
