using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>End-to-end behaviour of compiled clauses: unification, backtracking, cut, and tail calls.</summary>
public sealed class ExecutionTests
{
    [Fact]
    public void ReusedClauseCompilerResetsSlotsAndFailureState()
    {
        var engine = new PrologEngine();
        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(engine.Program, new ConstantPool(engine.Program), diagnostics, "reuse.pl");
        SyntaxTerm wideHead = Assert.Single(TermReader.ReadProgram("wide(X, A, B, C, D, E, F, G).").Clauses);
        SyntaxTerm condition = Assert.Single(TermReader.ReadProgram("(true -> !; fail).").Clauses);
        Assert.True(compiler.Compile(wideHead, condition) >= 0);

        SyntaxTerm narrowHead = Assert.Single(TermReader.ReadProgram("narrow(X).").Clauses);
        int narrow = compiler.Compile(narrowHead, null);
        Assert.True(narrow >= 0);
        CompiledProgramModel model = CompiledProgramModel.Create(engine.Program, narrow, [], []);
        Assert.Equal(OpCode.Allocate, model.Instructions[0].OpCode);
        Assert.Equal(1, model.Instructions[0].First);
        Assert.Equal(OpCode.GetVariable, model.Instructions[1].OpCode);
        Assert.Equal(0, model.Instructions[1].First);
        Assert.Empty(diagnostics);

        Assert.Equal(-1, compiler.Compile(new IntegerTerm(0, SourceSpan.None), null));
        Assert.Single(diagnostics);
        int recovered = compiler.Compile(new AtomTerm("recovered", SourceSpan.None), null);
        Assert.True(recovered >= 0);
        model = CompiledProgramModel.Create(engine.Program, recovered, [], []);
        Assert.Equal(OpCode.Allocate, model.Instructions[0].OpCode);
        Assert.Equal(0, model.Instructions[0].First);
    }

    [Fact]
    public void SiblingClausesKeepNamedAndAnonymousVariablesIndependent()
    {
        string output = PrologTestHost.Run(
            """
            choice(wide(X, A, B, C, D, E, F, G)) :- X = changed, fail.
            choice(pair(X, X)).
            choice(pair(_, _)).
            choice(last).
            :- initialization((findall(ok, (choice(T), T = pair(a, b)), Distinct),
                               findall(ok, (choice(T), T = pair(a, a)), Shared),
                               write(Distinct), write(Shared))).
            """
        );

        Assert.Equal("[ok][ok,ok]", output);
    }

    [Fact]
    public void ZeroArityClauseChainsResetBodyVariables()
    {
        Assert.Equal(
            "ok",
            PrologTestHost.Run(
                """
                p :- X = rejected, (true -> fail; X = unreachable).
                p :- X = ok, write(X).
                :- initialization(p).
                """
            )
        );
    }

    [Fact]
    public void UnifiesStructuresAndPropagatesBindings()
    {
        Assert.Equal("f(a,b)\n", PrologTestHost.RunGoal("X = f(a, Y), Y = b, write(X), nl"));
    }

    [Fact]
    public void UnifiesNestedStructuresInAClauseHead()
    {
        var output = PrologTestHost.Run(
            """
            unwrap(box(inner(Value)), Value).

            :- initialization((unwrap(box(inner(hello)), X), write(X), nl)).
            """
        );

        Assert.Equal("hello\n", output);
    }

    [Theory]
    [InlineData("flat(X, Y)", "pair(X, Y)", "flat(one, two)", "[pair(one,two)]")]
    [InlineData("flat(a, 7, X)", "X", "flat(a, 7, ok)", "[ok]")]
    [InlineData("outer(inner(deep(X)), side(X))", "X", "outer(inner(deep(ok)), side(ok))", "[ok]")]
    [InlineData("outer(inner(deep(X)), side(X))", "X", "outer(inner(deep(ok)), side(other))", "[]")]
    [InlineData(
        "nested(a(X), b(Y), c(X), d(Z), e(Y), f(Z))",
        "trio(X, Y, Z)",
        "nested(a(1), b(2), c(1), d(3), e(2), f(3))",
        "[trio(1,2,3)]"
    )]
    public void StructureHeadsPreserveBindingsAcrossDeferredMatching(string pattern, string result, string input, string expected)
    {
        string output = PrologTestHost.Run(
            $"""
            match({pattern}, {result}).
            :- initialization((findall(R, match({input}, R), Rs), write(Rs))).
            """
        );

        Assert.Equal(expected, output);
    }

    [Fact]
    public void DeferredHeadBindingsAreRestoredBeforeTryingTheNextClause()
    {
        string output = PrologTestHost.Run(
            """
            pick(tree(left(X), right(Y)), pair(Y, X), rejected) :- X = changed, fail.
            pick(tree(left(X), right(Y)), pair(Y, X), accepted).
            :- initialization((findall(pair(Tree, Result), (pick(Tree, Result, Kind), Kind = accepted), Rs),
                               Rs = [pair(tree(left(a), right(b)), pair(b, a))], write(Rs))).
            """
        );

        Assert.Equal("[pair(tree(left(a),right(b)),pair(b,a))]", output);
    }

    [Fact]
    public void RepeatedHeadVariableForcesArgumentsToMatch()
    {
        var output = PrologTestHost.Run(
            """
            same(X, X).

            :- initialization((same(a, a), write(yes), nl)).
            """
        );

        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void FailedGoalIsReportedRatherThanThrowing()
    {
        (RunResult result, var output, _) = PrologTestHost.Execute(
            """
            same(X, X).

            :- initialization(same(a, b)).
            """
        );

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void BacktracksThroughClauseAlternativesUntilAGoalSucceeds()
    {
        var output = PrologTestHost.Run(
            """
            p(1).
            p(2).
            p(3).

            :- initialization((p(X), X >= 3, write(X), nl)).
            """
        );

        Assert.Equal("3\n", output);
    }

    [Fact]
    public void BacktrackingUndoesBindingsMadeByTheFailedBranch()
    {
        var output = PrologTestHost.Run(
            """
            p(f(1)).
            p(f(2)).

            check(f(2)).

            :- initialization((p(X), check(X), write(X), nl)).
            """
        );

        Assert.Equal("f(2)\n", output);
    }

    [Fact]
    public void CutDiscardsRemainingAlternatives()
    {
        var output = PrologTestHost.Run(
            """
            p(1).
            p(2).
            p(3).

            first(X) :- p(X), !.

            :- initialization((first(X), write(X), nl)).
            """
        );

        Assert.Equal("1\n", output);
    }

    [Fact]
    public void CutIsLocalToItsOwnPredicate()
    {
        // The cut in first/1 must not remove the choice point q/1 created for the caller.
        var output = PrologTestHost.Run(
            """
            p(1).
            p(2).

            q(a).
            q(b).

            first(X) :- p(X), !.

            :- initialization((q(Q), Q = b, first(X), write(Q), write(X), nl)).
            """
        );

        Assert.Equal("b1\n", output);
    }

    [Theory]
    [InlineData("count(M)")]
    [InlineData("count(M), true")]
    [InlineData("(true, count(M)), true")]
    public void TailRecursionRunsAtConstantStackDepth(string tail)
    {
        // 200,000 iterations would exhaust the CLR stack if Prolog calls were CLR calls.
        var output = PrologTestHost.Run(
            $"""
            count(0) :- !.
            count(N) :- M is N - 1, {tail}.

            :- initialization((count(200000), write(done), nl)).
            """
        );

        Assert.Equal("done\n", output);
    }

    [Fact]
    public void RecursesOverListsBuiltFromHeadDecomposition()
    {
        var output = PrologTestHost.Run(
            """
            last([X], X).
            last([_|Tail], X) :- last(Tail, X).

            :- initialization((last([a,b,c], X), write(X), nl)).
            """
        );

        Assert.Equal("c\n", output);
    }

    [Fact]
    public void BuildsListsInGoalArguments()
    {
        Assert.Equal("[a,b,c]\n", PrologTestHost.RunGoal("X = [a,b,c], write(X), nl"));
    }

    [Fact]
    public void CallingAnUndefinedPredicateRaisesAnExistenceError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(nowhere(1)).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("existence_error(procedure, nowhere/1)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HaltStopsBeforeLaterGoalsRun()
    {
        (RunResult result, var output, _) = PrologTestHost.Execute(
            """
            :- initialization((write(before), nl, halt(3), write(after))).
            """
        );

        Assert.Equal(RunResult.Halted, result);
        Assert.Equal("before\n", output);
    }

    [Theory]
    [InlineData("halt(_)", "instantiation_error")]
    [InlineData("halt(stopped)", "type_error(integer,stopped)")]
    [InlineData("halt(1.0)", "type_error(integer,1.0)")]
    public void HaltRejectsInvalidExitStatus(string goal, string expected)
    {
        ArgumentNullException.ThrowIfNull(goal);

        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
    }

    [Fact]
    public void AGoalThatIsNotCallableRaisesACatchableRuntimeError()
    {
        var output = PrologTestHost.Run(
            """
            p :- 42.
            :- initialization(catch(p, error(E, _), write(E))).
            """
        );

        Assert.Equal("type_error(callable,42)", output);
    }

    [Fact]
    public void AnUnreachableNonCallableGoalDoesNotRaiseAnError()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.Run(
                """
                p :- fail, 42.
                :- initialization((\+ p, write(yes))).
                """
            )
        );
    }

    [Theory]
    [InlineData(":- module(shapes, [square/2]).")]
    [InlineData(":- discontiguous square/2.")]
    public void PortableDeclarationsAreAcceptedRatherThanRun(string declaration)
    {
        // A file written to load in any Prolog system opens with declarations this release does not
        // act on. Running them as goals would raise existence_error and make the file unusable.
        var output = PrologTestHost.Run(
            $"""
            {declaration}

            square(N, S) :- S is N * N.

            :- initialization((square(7, S), write(S), nl)).
            """
        );

        Assert.Equal("49\n", output);
    }

    [Fact]
    public void ReaderDiagnosticsSurfaceThroughConsult()
    {
        (_, _, IReadOnlyList<Diagnostic> diagnostics) = PrologTestHost.Execute("p(a b).");

        Assert.Equal(DiagnosticIds.UnexpectedToken, Assert.Single(diagnostics).Id);
    }
}
