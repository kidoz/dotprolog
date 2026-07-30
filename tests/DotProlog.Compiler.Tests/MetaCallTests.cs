using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// <c>call/1</c> and goals assembled at run time, including the bootstrap definitions that make the
/// control constructs reachable through a meta-call.
/// </summary>
public sealed class MetaCallTests
{
    [Fact]
    public void CallsAnAtomGoal()
    {
        string output = PrologTestHost.Run(
            """
            greet :- write(hi), nl.

            :- initialization(call(greet)).
            """
        );

        Assert.Equal("hi\n", output);
    }

    [Fact]
    public void CallsACompoundGoalAndPropagatesBindings()
    {
        string output = PrologTestHost.Run(
            """
            double(X, Y) :- Y is X * 2.

            :- initialization(( call(double(21, R)), write(R), nl )).
            """
        );

        Assert.Equal("42\n", output);
    }

    [Fact]
    public void CallsAGoalHeldInAVariable()
    {
        Assert.Equal("hello\n", PrologTestHost.RunGoal("G = write(hello), call(G), nl"));
    }

    [Fact]
    public void AVariableInGoalPositionIsAMetaCall()
    {
        string output = PrologTestHost.Run(
            """
            run(G) :- G.

            :- initialization(( run(write(direct)), nl )).
            """
        );

        Assert.Equal("direct\n", output);
    }

    [Fact]
    public void UnwrapsNestedCallWrappers()
    {
        Assert.Equal("deep\n", PrologTestHost.RunGoal("call(call(call(write(deep)))), nl"));
    }

    [Fact]
    public void CallsABuiltinGoal()
    {
        Assert.Equal("5\n", PrologTestHost.RunGoal("G = (X is 2 + 3), call(G), write(X), nl"));
    }

    [Fact]
    public void CallsAConjunctionBuiltAtRunTime()
    {
        Assert.Equal("ab\n", PrologTestHost.RunGoal("G = (write(a), write(b)), call(G), nl"));
    }

    [Fact]
    public void CallsADisjunctionBuiltAtRunTime()
    {
        Assert.Equal("2\n", PrologTestHost.RunGoal("G = ( X = 1 ; X = 2 ), call(G), X = 2, write(X), nl"));
    }

    [Fact]
    public void CallsAnIfThenElseBuiltAtRunTime()
    {
        Assert.Equal("small\n", PrologTestHost.RunGoal("G = ( 1 > 5 -> R = big ; R = small ), call(G), write(R), nl"));
    }

    [Fact]
    public void CallsASoftCutBuiltAtRunTime()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            :- initialization(( G = ( q(X) *-> true ; X = none ), call(G), X = 2, write(X), nl )).
            """
        );

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void CallsANegationBuiltAtRunTime()
    {
        // '\+' has priority 900, above the 699 that the right argument of '=' allows, so the goal
        // term needs its own parentheses — as it does in any ISO reader.
        Assert.Equal("yes\n", PrologTestHost.RunGoal("G = (\\+ (1 = 2)), call(G), write(yes), nl"));
    }

    [Fact]
    public void CallOfACutBehavesAsTrue()
    {
        // ISO: the cut in call(!) is local to the call, and there is nothing inside it to prune.
        Assert.Equal("ok\n", PrologTestHost.RunGoal("call(!), write(ok), nl"));
    }

    [Fact]
    public void CutInsideAMetaCalledGoalCommitsWithinTheMetaCall()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            :- initialization((
                G = ( q(X), ! ),
                ( call(G), X = 2 -> write(open) ; write(committed) ),
                nl
            )).
            """
        );

        Assert.Equal("committed\n", output);
    }

    [Fact]
    public void MetaCalledCutDoesNotPruneTheCallersAlternatives()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            pick(X) :- q(X), call((!, true)).

            :- initialization(( pick(X), X = 2, write(X), nl )).
            """
        );

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void RuntimeLoweringPreservesDistinctVariablesAndAliasing()
    {
        Assert.Equal("2\n", PrologTestHost.RunGoal("G = (X = Y, (Y = 1 ; Y = 2)), call(G), X = 2, write(Y), nl"));
    }

    [Fact]
    public void CallingAnUnboundGoalIsAnInstantiationError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(call(G)).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("instantiation_error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CallingANumberIsATypeError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(( G = 42, call(G) )).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("type_error(callable, 42)", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("once(_)", "instantiation_error")]
    [InlineData("once(4)", "type_error(callable,4)")]
    [InlineData("call(_, a)", "instantiation_error")]
    [InlineData("call(4, a)", "type_error(callable,4)")]
    public void ControlMetaCallsReportIsoGoalErrors(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
    }

    [Fact]
    public void CallEightEnforcesTheResultingMaximumArity()
    {
        Assert.Equal(
            "yes representation_error(max_arity)",
            PrologTestHost.RunGoal(
                "functor(Fact, call_limit, 255), assertz(Fact), "
                    + "functor(Allowed, call_limit, 248), "
                    + "call(Allowed, a, b, c, d, e, f, g), write(yes), "
                    + "abolish(call_limit/255), write(' '), "
                    + "functor(Oversized, call_limit, 249), "
                    + "catch(call(Oversized, a, b, c, d, e, f, g), error(E, _), write(E))"
            )
        );
    }

    [Fact]
    public void CallingAnUndefinedGoalIsAnExistenceError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(call(nowhere)).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("existence_error(procedure, nowhere/0)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MetaCallingTheSameControlShapeTwiceReusesTheCompiledClause()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Machine machine = engine.Machine;
        int conjunction = machine.Symbols.InternFunctor(",", 2);
        Cell yes = Cell.Atom(machine.Symbols.InternAtom("true"));
        var registers = new Cell[Machine.ArgumentRegisterCount];

        Cell first = machine.CreateStructure(conjunction, [yes, machine.CreateVariable()]);
        int address = engine.CompileControlGoal(machine, first, registers, out int arity);
        int size = engine.Program.CodeLength;

        Cell variable = machine.CreateVariable();
        Cell second = machine.CreateStructure(conjunction, [yes, variable]);
        int reused = engine.CompileControlGoal(machine, second, registers, out int reusedArity);

        Assert.Equal(address, reused);
        Assert.Equal(arity, reusedArity);
        Assert.Equal(size, engine.Program.CodeLength);
        Assert.Equal(variable, registers[0]);
    }

    [Fact]
    public void CachedControlShapesRunWithFreshBindings()
    {
        // G1 and G2 share one compiled clause; the second call must run with Y, not the X that the
        // first call already bound.
        Assert.Equal(
            "aaok\n",
            PrologTestHost.RunGoal("G1 = (X = a, write(X)), call(G1), G2 = (Y = a, write(Y)), call(G2), Y == a, write(ok), nl")
        );
    }

    [Fact]
    public void CachedControlShapesBacktrackIndependently()
    {
        Assert.Equal(
            "[1,2][1,2]\n",
            PrologTestHost.RunGoal(
                "G1 = (A = 1 ; A = 2), findall(A, call(G1), L1), "
                    + "G2 = (B = 1 ; B = 2), findall(B, call(G2), L2), write(L1), write(L2), nl"
            )
        );
    }
}
