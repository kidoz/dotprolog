using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Environment frames must stay protected while any live choice point still references them.
/// Deallocate lowers the stack top below such frames (last-call optimisation, or a clause that
/// ends in a control construct), so a later choice point must not record the lowered top as its
/// protection watermark — Allocate would reuse the frame's space and backtracking would resume
/// through a clobbered continuation, silently dropping solutions.
/// </summary>
public sealed class ChoicePointProtectionTests
{
    [Fact]
    public void SolutionsSurviveAUserPredicateCallAfterAnLcoReturn()
    {
        // The minimal shape: p/1 leaves a choice point referencing mid/1's frame, q/1 is reached
        // by last-call optimisation so the frame is deallocated, and out/1 — a user predicate,
        // because builtins allocate no frame — must not clobber it before backtracking.
        string output = PrologTestHost.Run(
            """
            p(a). p(b).
            q(a). q(b).

            mid(X) :- p(X), q(X).
            out(X) :- write(X), nl.

            main :- mid(X), out(X), fail.
            main :- write(done), nl.

            :- initialization(main).
            """
        );

        Assert.Equal("a\nb\ndone\n", output);
    }

    [Fact]
    public void ForallRunsACompoundActionForEverySolution()
    {
        string output = PrologTestHost.Run(
            """
            p(a). p(b).
            q(a). q(b).

            :- initialization(forall((p(X), q(X)), (write(X), nl))).
            """
        );

        Assert.Equal("a\nb\n", output);
    }

    [Fact]
    public void FindallCollectsThroughInClauseDisjunctionAndNegation()
    {
        string output = PrologTestHost.Run(
            """
            parent(tom, bob). parent(bob, ann).

            person(P) :- parent(P, _) ; parent(_, P).

            :- initialization((findall(P, (person(P), \+ parent(P, _)), L), write(L), nl)).
            """
        );

        Assert.Equal("[ann]\n", output);
    }

    [Fact]
    public void BacktrackingThroughAClauseEndingInADisjunctionReachesEverySolution()
    {
        // person/1 ends in a disjunction, so its frame is deallocated while the disjunction's
        // choice point is live; the negation's choice point then records the lowered stack top,
        // and the leaf/1 report must not clobber the frame before the remaining redos.
        string output = PrologTestHost.Run(
            """
            parent(tom, bob). parent(bob, ann).

            person(P) :- parent(P, _) ; parent(_, P).

            report(P) :- write(leaf(P)), nl.

            t :- person(P), write(P), nl, \+ parent(P, _), report(P), fail.
            t :- write(done), nl.

            :- initialization(t).
            """
        );

        Assert.Equal("tom\nbob\nbob\nann\nleaf(ann)\ndone\n", output);
    }

    [Fact]
    public void AnUncaughtBallIsNotSwallowedByBacktrackingThroughCatch()
    {
        // catch/3's deactivated frame is still referenced by p/1's choice point after the first
        // solution; if throw_if/1's frames clobber it, the redo path jumps past throw_if(2) and
        // the program "succeeds" instead of aborting.
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(
            engine
                .ConsultText(
                    """
                    p(1). p(2).

                    throw_if(2) :- throw(oops).
                    throw_if(_).

                    main :- catch(p(X), caught, true), throw_if(X), fail.
                    main :- write(done), nl.

                    :- initialization(main).
                    """
                )
                .Success
        );

        PrologException error = Assert.Throws<PrologException>(() => engine.RunPendingGoals());
        Assert.Equal("oops", error.Message);
    }
}
