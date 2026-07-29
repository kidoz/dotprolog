using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>ISO predicate indicators and enumeration of user-defined procedures.</summary>
public sealed class PredicateInformationTests
{
    [Fact]
    public void EnumeratesStaticPredicatesAndPartiallyInstantiatedIndicators()
    {
        string output = PrologTestHost.Run(
            """
            apple.
            dog(_).
            dog(_, _).

            :- initialization((
                current_predicate(apple/0),
                findall(A, current_predicate(dog/A), Arities),
                write(Arities), nl)).
            """
        );

        Assert.Equal("[1,2]\n", output);
    }

    [Fact]
    public void ExcludesNativeBundledAndInternalPredicates()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "\\+ current_predicate(write/1), "
                    + "\\+ current_predicate(member/2), "
                    + "\\+ current_predicate(catch/3), "
                    + "write(yes)"
            )
        );
    }

    [Fact]
    public void IncludesDeclaredEmptyPredicates()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.Run(
                """
                :- dynamic empty/1.
                :- initialization((current_predicate(empty/1), write(yes), nl)).
                """
            )
        );
    }

    [Fact]
    public void IncludesPredicatesCreatedByAssertAndRetractAll()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "assertz(asserted(a)), "
                    + "retractall(declared(_)), "
                    + "current_predicate(asserted/1), "
                    + "current_predicate(declared/1), "
                    + "write(yes)"
            )
        );
    }

    [Fact]
    public void AbolishedPredicateIsNoLongerDefinedOrCallable()
    {
        Assert.Equal(
            "gone",
            PrologTestHost.RunGoal(
                "assertz(gone(a)), "
                    + "abolish(gone/1), "
                    + "\\+ current_predicate(gone/1), "
                    + "catch(gone(_), error(existence_error(procedure, gone/1), _), write(gone))"
            )
        );
    }

    [Fact]
    public void CallStartedBeforeAbolitionKeepsItsLogicalUpdateView()
    {
        string output = PrologTestHost.Run(
            """
            :- dynamic p/1.
            p(1).
            p(2).

            :- initialization((
                findall(X, (p(X), (X = 1 -> abolish(p/1) ; true)), Values),
                write(Values), nl,
                \+ current_predicate(p/1))).
            """
        );

        Assert.Equal("[1,2]\n", output);
    }

    [Fact]
    public void CandidateFailureDoesNotBindASharedIndicatorVariable()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("\\+ current_predicate(X/X), var(X), write(yes)"));
    }

    [Theory]
    [InlineData("current_predicate(4)", "type_error(predicate_indicator,4)")]
    [InlineData("current_predicate(foo/a)", "type_error(integer,a)")]
    [InlineData("current_predicate(4/1)", "type_error(atom,4)")]
    [InlineData("current_predicate(foo/(-1))", "domain_error(not_less_than_zero,-1)")]
    [InlineData("current_predicate(foo/256)", "representation_error(max_arity)")]
    public void ReportsIsoPredicateIndicatorErrors(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
    }

    [Fact]
    public void AbolishRejectsAStaticProcedure()
    {
        string output = PrologTestHost.Run(
            """
            fixed.
            :- initialization(catch(abolish(fixed/0), error(E, _), write(E))).
            """
        );

        Assert.Equal("permission_error(modify,static_procedure,fixed/0)", output);
    }
}
