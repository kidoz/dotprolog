using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Definite clause grammars: the translation of <c>--&gt;/2</c> into ordinary clauses, and
/// <c>phrase/2,3</c>.
/// </summary>
public sealed class GrammarTests
{
    private const string Grammar = """
        greeting --> [hello], name.
        name --> [world].
        name --> [prolog].

        digits([D|T]) --> digit(D), digits(T).
        digits([D])   --> digit(D).
        digit(D)      --> [D], { D >= 0'0, D =< 0'9 }.

        number(N) --> digits(Ds), { number_codes(N, Ds) }.

        nothing --> [].
        two --> [a, b].
        either --> [a] ; [b].
        optional(X) --> ( [X] -> [] ; { X = none } ).
        not_x --> \+ [x], [_].
        greedy([H|T]) --> [H], !, greedy(T).
        greedy([]) --> [].

        """;

    private static string Run(string goal) => PrologTestHost.Run($"{Grammar}:- initialization(({goal})).\n");

    [Theory]
    [InlineData("phrase(greeting, [hello, world])")]
    [InlineData("phrase(greeting, [hello, prolog])")]
    [InlineData("phrase(nothing, [])")]
    [InlineData("phrase(two, [a, b])")]
    [InlineData("phrase(either, [a])")]
    [InlineData("phrase(either, [b])")]
    [InlineData("phrase(not_x, [y])")]
    public void Accepts(string goal) => Assert.Equal("yes", Run($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("phrase(greeting, [hello, there])")]
    [InlineData("phrase(greeting, [hello])")]
    [InlineData("phrase(greeting, [hello, world, extra])")]
    [InlineData("phrase(nothing, [a])")]
    [InlineData("phrase(two, [a])")]
    [InlineData("phrase(either, [c])")]
    [InlineData("phrase(not_x, [x])")]
    public void Rejects(string goal) => Assert.Equal("no", Run($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void ANonTerminalEnumeratesItsAlternatives() =>
        Assert.Equal("[world,prolog]", Run("findall(N, phrase(name, [N]), L), write(L)"));

    [Fact]
    public void ABracedGoalRunsWithoutConsuming() => Assert.Equal("427", Run("phrase(number(N), \"427\"), write(N)"));

    [Fact]
    public void TerminalsAreMatchedAgainstCodeLists() =>
        Assert.Equal("[52,50,55]-[]", Run("phrase(digits(D), \"427\", R), write(D-R)"));

    [Fact]
    public void PhraseThreeReportsWhatIsLeft() =>
        Assert.Equal("[hello,world]-[extra]", Run("phrase(greeting, [hello, world, extra], R), write([hello,world]-R)"));

    [Fact]
    public void ARuleLeavesEveryRemainderOnBacktracking() =>
        Assert.Equal("[[],[50]]", Run("findall(S, phrase(digits(_), \"12\", S), L), write(L)"));

    [Fact]
    public void AnIfThenElseChoosesWithoutConsumingOnTheElseBranch() =>
        Assert.Equal("none", Run("phrase(optional(X), [], _), write(X)"));

    [Fact]
    public void ACutInARuleCommitsToIt() => Assert.Equal("[1,2,3]", Run("phrase(greedy(G), [1, 2, 3]), write(G)"));

    [Fact]
    public void APushbackListPutsTerminalsBackOntoTheInput()
    {
        // a, [b] --> [x] consumes an x and leaves a b in its place, which is what a pushback list is
        // for: rewriting the input the rules that follow will see.
        Assert.Equal(
            "[b,y]",
            PrologTestHost.Run(
                """
                a, [b] --> [x].
                :- initialization((phrase(a, [x, y], R), write(R))).
                """
            )
        );
    }

    [Fact]
    public void AVariableBodyGoesThroughPhrase() =>
        Assert.Equal("yes", Run("X = [a], ( phrase(X, [a]) -> write(yes) ; write(no) )"));

    [Fact]
    public void PhraseWalksControlConstructsInABodyBuiltAtRunTime() =>
        Assert.Equal("yes", Run("Body = ([hello], name), ( phrase(Body, [hello, world]) -> write(yes) ; write(no) )"));

    [Fact]
    public void AnAssertedGrammarRuleIsTranslatedToo() =>
        Assert.Equal("yes", PrologTestHost.RunGoal("assertz((late --> [z])), ( phrase(late, [z]) -> write(yes) ; write(no) )"));

    [Fact]
    public void PhraseOnAnUnboundBodyIsReported() =>
        Assert.Equal("instantiation_error", PrologTestHost.RunGoal("catch(phrase(_, []), error(E, _), write(E))"));

    [Fact]
    public void GeneratedVariablesCannotCollideWithTheGrammarsOwn()
    {
        // The translator's variables are named so that no lexer can produce them. A clause's
        // variables are keyed by name, so a rule whose own variables are called S0 and S would
        // otherwise have them merged with the two the translation adds.
        Assert.Equal(
            "a-seen(a)-[]",
            PrologTestHost.Run(
                """
                keep(S0, S) --> [S0], { S = seen(S0) }.
                :- initialization((phrase(keep(A, B), [a], R), write(A-B-R))).
                """
            )
        );
    }

    [Fact]
    public void ARuleHeadThatIsNotANonTerminalIsReported()
    {
        (RunResult result, _, IReadOnlyList<Syntax.Diagnostic> diagnostics) = PrologTestHost.Execute("1 --> [a].\n");

        Assert.NotEqual(RunResult.Success, result);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidGrammarRule);
    }

    [Fact]
    public void ANumberInARuleBodyIsReported()
    {
        (_, _, IReadOnlyList<Syntax.Diagnostic> diagnostics) = PrologTestHost.Execute("a --> 1.\n");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidGrammarRule);
    }
}
