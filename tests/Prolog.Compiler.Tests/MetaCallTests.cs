using Prolog.Runtime;

namespace Prolog.Compiler.Tests;

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
    public void CutInsideAMetaCalledGoalDoesNotPruneTheMetaCall()
    {
        // Documented deviation: a cut inside a goal reached through call/1 prunes nothing, so this
        // still yields both solutions. Reaching ISO behaviour needs a call barrier the engine lacks.
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            :- initialization(( G = ( q(X), ! ), call(G), X = 2, write(X), nl )).
            """
        );

        Assert.Equal("2\n", output);
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

    [Fact]
    public void CallingAnUndefinedGoalIsAnExistenceError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(call(nowhere)).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("existence_error(procedure, nowhere/0)", exception.Message, StringComparison.Ordinal);
    }
}
