namespace Prolog.Compiler.Tests;

/// <summary><c>findall/3</c> and <c>forall/2</c>, which are built on the solution-collection buffer.</summary>
public sealed class FindallTests
{
    private const string Facts = """
        p(1).
        p(2).
        p(3).
        """;

    [Fact]
    public void CollectsEverySolution()
    {
        Assert.Equal("[1,2,3]\n", PrologTestHost.Run($"{Facts}\n:- initialization((findall(X, p(X), L), write(L), nl))."));
    }

    [Fact]
    public void CollectsNothingForAGoalThatFails()
    {
        Assert.Equal("[]\n", PrologTestHost.RunGoal("findall(X, fail, L), write(L), nl"));
    }

    [Fact]
    public void CollectsAnInstantiatedTemplateRatherThanTheGoal()
    {
        string output = PrologTestHost.Run(
            $"""
            {Facts}

            :- initialization((findall(item(X), p(X), L), write(L), nl)).
            """
        );

        Assert.Equal("[item(1),item(2),item(3)]\n", output);
    }

    [Fact]
    public void CollectsAcrossAConjunctiveGoal()
    {
        string output = PrologTestHost.Run(
            $"""
            {Facts}

            :- initialization((findall(X-Y, (p(X), p(Y), X < Y), L), write(L), nl)).
            """
        );

        Assert.Equal("[-(1,2),-(1,3),-(2,3)]\n", output);
    }

    [Fact]
    public void LeavesTheTemplateVariableUnboundAfterwards()
    {
        string output = PrologTestHost.Run(
            $"""
            {Facts}

            :- initialization((findall(X, p(X), _), var(X), write(unbound), nl)).
            """
        );

        Assert.Equal("unbound\n", output);
    }

    [Fact]
    public void RenamesVariablesApartBetweenSolutions()
    {
        // Two solutions that are both unbound must not come back sharing one variable.
        string output = PrologTestHost.Run(
            """
            q(_).
            q(_).

            :- initialization((findall(X, q(X), [A,B]), A \== B, write(distinct), nl)).
            """
        );

        Assert.Equal("distinct\n", output);
    }

    [Fact]
    public void NestsWithoutTheInnerCollectionDisturbingTheOuter()
    {
        string output = PrologTestHost.Run(
            """
            p(1).
            p(2).

            :- initialization((findall(X-L, (p(X), findall(Y, p(Y), L)), R), write(R), nl)).
            """
        );

        Assert.Equal("[-(1,[1,2]),-(2,[1,2])]\n", output);
    }

    [Fact]
    public void AThrowFromInsideFindallDoesNotStrandTheCollection()
    {
        // The collection depth is recorded on every choice point, so unwinding restores it and the
        // next findall starts clean rather than appending to an abandoned buffer.
        string output = PrologTestHost.Run(
            $"""
            {Facts}

            :- initialization((
                   catch(findall(X, (p(X), throw(boom)), _), boom, true),
                   findall(Y, p(Y), L),
                   write(L), nl
               )).
            """
        );

        Assert.Equal("[1,2,3]\n", output);
    }

    [Fact]
    public void UnifiesWithAnAlreadyBoundBag()
    {
        Assert.Equal("yes", PrologTestHost.Run($"{Facts}\n:- initialization((findall(X, p(X), [1,2,3]), write(yes)))."));
    }

    [Fact]
    public void FailsWhenTheBagDoesNotMatch()
    {
        Assert.Equal("yes", PrologTestHost.Run($"{Facts}\n:- initialization((\\+ findall(X, p(X), [1,2]), write(yes)))."));
    }

    [Fact]
    public void ForallHoldsWhenEverySolutionSatisfiesTheAction()
    {
        Assert.Equal("yes", PrologTestHost.Run($"{Facts}\n:- initialization((forall(p(X), X > 0), write(yes)))."));
    }

    [Fact]
    public void ForallFailsWhenOneSolutionDoesNot()
    {
        Assert.Equal("yes", PrologTestHost.Run($"{Facts}\n:- initialization((\\+ forall(p(X), X > 1), write(yes)))."));
    }
}
