namespace DotProlog.Compiler.Tests;

/// <summary><c>op/3</c> and <c>current_op/3</c>, and when a declaration takes effect.</summary>
public sealed class OperatorDeclarationTests
{
    [Fact]
    public void ADeclaredOperatorIsUsableLaterInTheSameFile()
    {
        // This is the whole point of applying the directive while reading: a file that declares an
        // operator has to be able to use it, and the rest of the file is read before any goal runs.
        Assert.Equal(
            "likes(alice,bob)",
            PrologTestHost.Run(
                """
                :- op(700, xfx, likes).

                fact(alice likes bob).

                :- initialization((fact(F), write_canonical(F))).
                """
            )
        );
    }

    [Fact]
    public void AQuotedDeclaredOperatorIsUsableLaterInTheSameFile()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.Run(
                """
                :- op(650, xfx, quoted_infix).

                fact(left 'quoted_infix' right).

                :- initialization((fact(quoted_infix(left, right)), write(yes))).
                """
            )
        );
    }

    [Fact]
    public void RuntimeTermInputUsesQuotedOperatorNames()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "op(650, xfx, quoted_infix), "
                    + "read_term_from_atom('left ''quoted_infix'' right', quoted_infix(left, right), []), "
                    + "op(0, xfx, quoted_infix), write(yes)"
            )
        );
    }

    [Fact]
    public void ADeclaredOperatorIsUsedWhenWriting() =>
        Assert.Equal(
            "alice likes bob",
            PrologTestHost.Run(
                """
                :- op(700, xfx, likes).
                :- initialization((X = likes(alice, bob), write(X))).
                """
            )
        );

    [Theory]
    [InlineData("op(200, xfy, knows), X = knows(a, b), write(X)", "a knows b")]
    [InlineData("op(9, fy, very), X = very(good), write(X)", "very good")]
    [InlineData("op(100, xf, factorial), X = factorial(3), write(X)", "3 factorial")]
    [InlineData("op(700, xfx, [eq, ne]), X = eq(a, b), write(X)", "a eq b")]
    public void DeclaresOperatorsOfEveryClass(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void APriorityOfZeroRemovesTheDefinition() =>
        Assert.Equal("+(1,2)", PrologTestHost.RunGoal("op(0, yfx, +), X = 1 + 2, write(X)"));

    [Theory]
    [InlineData("op(_, xfx, foo)", "instantiation_error")]
    [InlineData("op(700, xfx, _)", "instantiation_error")]
    [InlineData("op(a, xfx, foo)", "type_error(integer,a)")]
    [InlineData("op(1300, xfx, foo)", "domain_error(operator_priority,1300)")]
    [InlineData("op(-1, xfx, foo)", "domain_error(operator_priority,-1)")]
    [InlineData("op(700, nonsense, foo)", "domain_error(operator_specifier,nonsense)")]
    [InlineData("op(700, 7, foo)", "type_error(atom,7)")]
    [InlineData("op(700, xfx, 7)", "type_error(atom,7)")]
    // The culprit prints as (,) rather than ',' because write/1 does not quote, and an operator
    // atom in an argument position is bracketed so that the output still reads back.
    [InlineData("op(700, xfx, ',')", "permission_error(modify,operator,(,)/2)")]
    public void RejectsBadDeclarations(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Fact]
    public void CurrentOpFindsADefinition() =>
        Assert.Equal("500-yfx", PrologTestHost.RunGoal("current_op(P, T, +), P =:= 500, write(P-T)"));

    [Fact]
    public void CurrentOpScansPastNonMatchingDefinitions() =>
        Assert.Equal("yes", PrologTestHost.RunGoal("current_op(1200, xfx, '-->'), write(yes)"));

    [Fact]
    public void CurrentOpEnumeratesEveryDefinition()
    {
        // The ISO table this engine starts with; the count is asserted so that adding an operator to
        // the defaults has to be a deliberate edit here as well.
        Assert.Equal("54", PrologTestHost.RunGoal("findall(N, current_op(_, _, N), L), length(L, C), write(C)"));
    }

    [Theory]
    [InlineData("current_op(a, _, _)", "domain_error(operator_priority,a)")]
    [InlineData("current_op(1201, _, _)", "domain_error(operator_priority,1201)")]
    [InlineData("current_op(_, 1, _)", "domain_error(operator_specifier,1)")]
    [InlineData("current_op(_, nonsense, _)", "domain_error(operator_specifier,nonsense)")]
    [InlineData("current_op(_, _, 1)", "type_error(atom,1)")]
    public void CurrentOpRejectsInvalidFilters(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Fact]
    public void CurrentOpSeesWhatOpDeclared() =>
        Assert.Equal("yes", PrologTestHost.RunGoal("op(333, xfx, zzz), current_op(333, xfx, zzz), write(yes)"));

    [Fact]
    public void CurrentOpKeepsRemovedDefinitionsInItsSnapshot() =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "op(333, xfx, snapshot_old), "
                    + "findall(N, (current_op(_, _, N), op(0, xfx, snapshot_old)), Names), "
                    + "member(snapshot_old, Names), write(yes)"
            )
        );

    [Fact]
    public void CurrentOpDoesNotAddNewDefinitionsToItsSnapshot() =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "findall(N, (current_op(_, _, N), op(333, xfx, snapshot_new)), Names), "
                    + "\\+ member(snapshot_new, Names), write(yes)"
            )
        );

    [Fact]
    public void OperatorsDeclaredInOneEngineDoNotLeakIntoAnother()
    {
        // The table belongs to the program, not to a static, so two engines in one process cannot
        // change each other's syntax.
        Assert.Equal("a likes b", PrologTestHost.RunGoal("op(700, xfx, likes), X = likes(a, b), write(X)"));
        Assert.Equal("likes(a,b)", PrologTestHost.RunGoal("X = likes(a, b), write(X)"));
    }
}
