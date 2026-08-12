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
    [InlineData("current_output(S), format(S, \"handle\", [])", "handle")]
    public void WritesToASink(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void UserErrorTargetsTheErrorStream()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var engine = new PrologEngine { Output = output, Error = error };

        Assert.True(engine.ConsultText(":- initialization(with_output_to(atom(A), format(user_error, \"oops\", []))).").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());

        // The error text bypasses the capture instead of being folded into the current output.
        Assert.Equal("oops", error.ToString());
        Assert.Equal("", output.ToString());
    }

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
    public void LeftoverArgumentsAreReported() =>
        Assert.Equal(
            "domain_error(format_arguments,[a,b])",
            PrologTestHost.RunGoal("catch(format(\"~w\", [a, b]), error(E, _), write(E))")
        );

    [Fact]
    public void TabWritesSpaces() => Assert.Equal("a  b", PrologTestHost.RunGoal("write(a), tab(1 + 1), write(b)"));

    [Fact]
    public void TabRejectsAFloatCount() =>
        Assert.Equal("type_error(integer,1.5)", PrologTestHost.RunGoal("catch(tab(1.5), error(E, _), write(E))"));

    [Theory]
    [InlineData("format(nowhere, \"x\", [])", "existence_error(stream,nowhere)")]
    [InlineData("format(7, \"x\", [])", "domain_error(stream_or_alias,7)")]
    [InlineData("format(user_input, \"x\", [])", "permission_error(output,stream,user_input)")]
    public void FormatToABadStreamIsReported(string goal, string expected)
    {
        (RunResult result, var output, _) = PrologTestHost.Execute($":- initialization(catch({goal}, error(E, _), write(E))).");

        Assert.Equal(RunResult.Success, result);
        Assert.Equal(expected, output);
    }

    // ~@ runs its goal once and splices the goal's output in place; the goal's
    // failure fails the whole format, and its ball propagates.
    [Theory]
    [InlineData("format(\"a~@b\", [write(mid)])", "amidb")]
    [InlineData("format(\"~@~@\", [write(1), write(2)])", "12")]
    [InlineData("format(\"~w~@~w\", [l, write(m), r])", "lmr")]
    [InlineData("format(\"~@\", [member(X, [1, 2])]), format(\"~w\", [X])", "1")]
    [InlineData("format(atom(A), \"x~@y\", [write(q)]), write(A)", "xqy")]
    public void FormatAtRunsTheGoalInPlace(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    // SWI streams directives, so the text before the failing goal has already been written.
    [Fact]
    public void FormatAtGoalFailureFailsTheFormatAfterItsPrefix() =>
        Assert.Equal("xno", PrologTestHost.RunGoal("( format(\"x~@\", [fail]) -> write(yes) ; write(no) )"));

    [Fact]
    public void FormatAtGoalBallPropagates() =>
        Assert.Equal("caught(ball)", PrologTestHost.RunGoal("catch(format(\"~@\", [throw(ball)]), B, write(caught(B)))"));

    // ~W writes its first argument under its second argument's write_term options.
    [Theory]
    [InlineData("format(\"~W\", [f('A b'), []])", "f(A b)")]
    [InlineData("format(\"~W\", [f('A b'), [quoted(true)]])", "f('A b')")]
    [InlineData("format(\"~W\", [[1, 2], [spacing(next_argument)]])", "[1, 2]")]
    [InlineData("format(\"~W\", [1 + 2, [ignore_ops(true)]])", "+(1,2)")]
    [InlineData("format(\"~W\", ['$VAR'(0), [numbervars(true)]])", "A")]
    public void FormatWWritesUnderOptions(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void FormatWRejectsABadOption() =>
        Assert.Equal(
            "domain_error(write_option,bogus(true))",
            PrologTestHost.RunGoal("catch(format(\"~W\", [x, [bogus(true)]]), error(E, _), true), write(E)")
        );

    // portray_clause/1,2 emit SWI's listing layout; the outputs are byte-identical
    // to SWI-Prolog 10 for every case here.
    [Theory]
    [InlineData("portray_clause(foo)", "foo.\n")]
    [InlineData("portray_clause((p :- true))", "p.\n")]
    [InlineData("portray_clause(f('A b', [1, 2]))", "f('A b', [1, 2]).\n")]
    [InlineData("portray_clause((a = b))", "a=b.\n")]
    [InlineData("portray_clause((foo(V, W) :- bar(V), baz(W)))", "foo(A, B) :-\n    bar(A),\n    baz(B).\n")]
    [InlineData("portray_clause((p(Q) :- q(Q)))", "p(A) :-\n    q(A).\n")]
    [InlineData("portray_clause(p(Q))", "p(_).\n")]
    [InlineData("portray_clause((p :- (a ; b)))", "p :-\n    (   a\n    ;   b\n    ).\n")]
    [InlineData("portray_clause((p :- (a -> b ; c)))", "p :-\n    (   a\n    ->  b\n    ;   c\n    ).\n")]
    [InlineData("portray_clause((p :- q, (a, b ; c)))", "p :-\n    q,\n    (   a,\n        b\n    ;   c\n    ).\n")]
    [InlineData(
        "portray_clause((p :- (a -> b ; c -> d ; e)))",
        "p :-\n    (   a\n    ->  b\n    ;   c\n    ->  d\n    ;   e\n    ).\n"
    )]
    [InlineData("portray_clause((p :- \\+ q(Z), r(Z)))", "p :-\n    \\+ q(A),\n    r(A).\n")]
    [InlineData("portray_clause((p :- (a ; b), c))", "p :-\n    (   a\n    ;   b\n    ),\n    c.\n")]
    public void PortrayClauseEmitsSwiListingLayout(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void PortrayClauseWritesToAnExplicitStream() =>
        Assert.Equal("ok.\n", PrologTestHost.RunGoal("current_output(S), portray_clause(S, ok)"));
}
