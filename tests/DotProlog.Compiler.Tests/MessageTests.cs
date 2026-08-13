using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// print_message/2: SWI's message system sized to this machine. The message term is translated
/// to Format-Args lines, a user-defined message_hook/3 may intercept them, and what remains is
/// written to the error stream behind the kind's prefix. The expected texts are SWI 10's, probed
/// without its location and thread decorations.
/// </summary>
public sealed class MessageTests
{
    /// <summary>Runs <paramref name="goal"/> and returns what it wrote to the two streams.</summary>
    private static (string Output, string Error) RunCapturingError(string source)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var engine = new PrologEngine { Output = output, Error = error };

        Assert.True(engine.ConsultText(source, "messages.pl").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        return (output.ToString(), error.ToString());
    }

    private static string ErrorOf(string goal) => RunCapturingError($":- initialization(({goal})).\n").Error;

    [Theory]
    [InlineData(
        "print_message(error, error(type_error(atom, 1), context(foo/2, _)))",
        "ERROR: foo/2: Type error: `atom' expected, found `1' (an integer)\n"
    )]
    [InlineData(
        "print_message(error, error(instantiation_error, context(foo/2, _)))",
        "ERROR: foo/2: Arguments are not sufficiently instantiated\n"
    )]
    [InlineData(
        "print_message(error, error(domain_error(order, x), context(compare/3, _)))",
        "ERROR: compare/3: Domain error: `order' expected, found `x'\n"
    )]
    [InlineData(
        "print_message(error, error(domain_error(range(1, 3), 5), context(f/1, _)))",
        "ERROR: f/1: Domain error: [1..3] expected, found `5'\n"
    )]
    [InlineData(
        "print_message(error, error(existence_error(procedure, foo/2), context(bar/1, _)))",
        "ERROR: bar/1: Unknown procedure: foo/2\n"
    )]
    [InlineData(
        "print_message(error, error(existence_error(stream, mystream), context(close/1, _)))",
        "ERROR: close/1: stream `mystream' does not exist\n"
    )]
    [InlineData(
        "print_message(error, error(permission_error(modify, static_procedure, foo/2), context(assertz/1, _)))",
        "ERROR: assertz/1: No permission to modify static procedure `foo/2'\n"
    )]
    [InlineData(
        "print_message(error, error(permission_error(input, stream, user_output), context(get_char/2, _)))",
        "ERROR: get_char/2: No permission to read from output stream `user_output'\n"
    )]
    [InlineData(
        "print_message(error, error(permission_error(open, source_sink, alias(x)), context(open/4, _)))",
        "ERROR: open/4: No permission to reuse alias \"x\": already taken\n"
    )]
    [InlineData(
        "print_message(error, error(permission_error(modify, flag, bounded), context(set_prolog_flag/2, _)))",
        "ERROR: set_prolog_flag/2: No permission to modify flag `bounded'\n"
    )]
    [InlineData(
        "print_message(error, error(evaluation_error(zero_divisor), context((is)/2, _)))",
        "ERROR: is/2: Arithmetic: evaluation error: `zero_divisor'\n"
    )]
    [InlineData(
        "print_message(error, error(type_error(evaluable, foo/0), context((is)/2, _)))",
        "ERROR: is/2: Arithmetic: `foo/0' is not a function\n"
    )]
    [InlineData(
        "print_message(error, error(representation_error(max_arity), context(f/1, _)))",
        "ERROR: f/1: Cannot represent due to `max_arity'\n"
    )]
    [InlineData(
        "print_message(error, error(resource_error(memory), context(f/1, _)))",
        "ERROR: f/1: Not enough resources: memory\n"
    )]
    [InlineData(
        "print_message(error, error(syntax_error(operator_expected), context(read/1, _)))",
        "ERROR: read/1: Syntax error: Operator expected\n"
    )]
    [InlineData(
        "print_message(error, error(syntax_error(cannot_start_term), _))",
        "ERROR: Syntax error: Illegal start of term\n"
    )]
    [InlineData("print_message(error, error(syntax_error(float_overflow), _))", "ERROR: Syntax error: float_overflow\n")]
    [InlineData(
        "print_message(error, error(uninstantiation_error(bound), context(open/4, _)))",
        "ERROR: open/4: Uninstantiated argument expected, found bound\n"
    )]
    [InlineData(
        "print_message(error, error(weird_error(a, b), context(f/1, _)))",
        "ERROR: f/1: Unknown error term: weird_error(a,b)\n"
    )]
    [InlineData(
        "print_message(error, error(type_error(atom, 1), context(foo/2, 'extra note')))",
        "ERROR: foo/2: Type error: `atom' expected, found `1' (an integer) (extra note)\n"
    )]
    [InlineData(
        "print_message(error, error(type_error(atom, 1), context(lists:member/2, _)))",
        "ERROR: lists:member/2: Type error: `atom' expected, found `1' (an integer)\n"
    )]
    [InlineData(
        "print_message(error, error(type_error(atom, 1), _))",
        "ERROR: Type error: `atom' expected, found `1' (an integer)\n"
    )]
    public void TranslatesTheErrorTermAsSwiDoes(string goal, string expected) => Assert.Equal(expected, ErrorOf(goal));

    // The parenthesized classification of the culprit, in SWI's wording: [] stays an
    // empty_list even though this machine classifies [] as an atom elsewhere.
    [Theory]
    [InlineData("abc", "(an atom)")]
    [InlineData("1.5", "(a float)")]
    [InlineData("f(x)", "(a compound)")]
    [InlineData("[a, b]", "(a list)")]
    [InlineData("[]", "(an empty_list)")]
    [InlineData("[a|_]", "(a partial_list)")]
    [InlineData("[a|b]", "(an invalid_list)")]
    [InlineData("_", "(a var)")]
    public void ClassifiesTheCulprit(string culprit, string classification) =>
        Assert.EndsWith(
            $"{classification}\n",
            ErrorOf($"print_message(error, error(type_error(integer, {culprit}), context(foo/2, _)))")
        );

    [Fact]
    public void ClassifiesAStringCulprit() =>
        Assert.Equal(
            "ERROR: foo/2: Type error: `atom' expected, found `\"str\"' (a string)\n",
            ErrorOf("atom_string(str, S), print_message(error, error(type_error(atom, S), context(foo/2, _)))")
        );

    [Fact]
    public void TranslatesTheOccursCheckError()
    {
        var error = ErrorOf("print_message(error, error(occurs_check(V, f(V)), context((=)/2, _)))");

        Assert.StartsWith("ERROR: =/2: Cannot unify _", error);
        Assert.EndsWith(": would create an infinite tree\n", error);
    }

    [Theory]
    [InlineData("print_message(warning, format('hello ~w', [world]))", "Warning: hello world\n")]
    [InlineData("print_message(informational, format('hi there', []))", "% hi there\n")]
    [InlineData("print_message(help, format('help ~w', [4]))", "help 4\n")]
    [InlineData("print_message(some_kind, format('x', []))", "x\n")]
    [InlineData("print_message(error, format('a ~w', [1]))", "ERROR: a 1\n")]
    [InlineData("print_message(silent, format('never', []))", "")]
    [InlineData("print_message(debug(topic), format('never', []))", "")]
    [InlineData("print_message(error, hello_world(42))", "ERROR: Unknown message: hello_world(42)\n")]
    public void PrintsTheKindPrefixOnTheErrorStream(string goal, string expected) => Assert.Equal(expected, ErrorOf(goal));

    [Fact]
    public void AnUnboundMessageIsUnknownRatherThanAnError()
    {
        var error = ErrorOf("print_message(error, _)");

        Assert.StartsWith("ERROR: Unknown message: _", error);
    }

    [Fact]
    public void AnUnboundKindIsAnInstantiationError() =>
        Assert.Equal("instantiation_error", PrologTestHost.RunGoal("catch(print_message(_, foo), error(E, _), true), write(E)"));

    [Fact]
    public void TheMessageTextBypassesTheCurrentOutput()
    {
        (var output, var error) = RunCapturingError(
            ":- initialization((write(before), print_message(error, format(mid, [])), write(after))).\n"
        );

        Assert.Equal("beforeafter", output);
        Assert.Equal("ERROR: mid\n", error);
    }

    [Fact]
    public void AMessageHookInterceptsThePrinting()
    {
        (var output, var error) = RunCapturingError(
            """
            :- initialization((
                assertz((message_hook(hooked, Kind, _) :- write(saw(Kind)))),
                print_message(warning, hooked)
            )).
            """
        );

        Assert.Equal("saw(warning)", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void AFailingMessageHookFallsBackToTheDefaultPrinting()
    {
        (var output, var error) = RunCapturingError(
            """
            :- initialization((
                assertz((message_hook(_, _, _) :- fail)),
                print_message(error, format(still, []))
            )).
            """
        );

        Assert.Equal("", output);
        Assert.Equal("ERROR: still\n", error);
    }

    // The hook receives the same Format-Args lines the default printer would write,
    // so a host can render the translation anywhere it wants.
    [Fact]
    public void TheHookReceivesTheTranslatedLines()
    {
        (var output, var error) = RunCapturingError(
            """
            :- initialization((
                assertz((message_hook(_, _, Lines) :- forall(member(F-A, Lines), format(F, A)))),
                print_message(error, error(type_error(atom, 1), context(foo/2, _)))
            )).
            """
        );

        Assert.Equal("foo/2: Type error: `atom' expected, found `1' (an integer)", output);
        Assert.Equal("", error);
    }

    // silent is decided before the hook, as SWI decides it: the hook never sees a
    // message that could not print.
    [Fact]
    public void TheHookIsNotConsultedForSilentMessages()
    {
        (var output, var error) = RunCapturingError(
            """
            :- initialization((
                assertz((message_hook(_, _, _) :- write(hooked))),
                print_message(silent, anything)
            )).
            """
        );

        Assert.Equal("", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void StrictModeRejectsPrintMessage()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p :- print_message(error, foo).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("print_message/2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModernModeSharesPrintMessage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = output, Error = error };

        Assert.True(engine.ConsultText(":- initialization(print_message(warning, format('w', []))).").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("Warning: w\n", error.ToString());
    }
}
