using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The rational number tier: canonical interned fractions reached through <c>1r3</c> literals and
/// <c>rdiv/2</c>, exact when mixed with integers, widening to double beside floats, and demoting
/// to integers when the denominator divides out. The expected behavior is SWI-Prolog 10's, probed.
/// </summary>
public sealed class RationalTests
{
    [Theory]
    [InlineData("X is 1 rdiv 3", "1r3")]
    [InlineData("X is 2 rdiv 4", "1r2")]
    [InlineData("X is 4 rdiv 2", "2")]
    [InlineData("X is 1 rdiv (10 ^ 30)", "1r1000000000000000000000000000000")]
    [InlineData(
        "X is (10 ^ 30) rdiv (2 ^ 100), Y is X * (2 ^ 100) rdiv (10 ^ 30), X == X, Y =:= 1, X = X",
        "931322574615478515625r1180591620717411303424"
    )]
    [InlineData("X = 1r3", "1r3")]
    [InlineData("X = 2r4", "1r2")]
    [InlineData("X = -1r3", "-1r3")]
    [InlineData("X is 1r3 + 1", "4r3")]
    [InlineData("X is 1r3 + 1r6", "1r2")]
    [InlineData("X is 1r3 * 3", "1")]
    [InlineData("X is 1r3 / 2", "1r6")]
    [InlineData("X is 1r2 + 0.5", "1.0")]
    [InlineData("X is -(1r3)", "-1r3")]
    [InlineData("X is 1r2 ^ 2", "1r4")]
    [InlineData("X is 1r2 ^ (-2)", "4")]
    [InlineData("X is abs(-1r3)", "1r3")]
    [InlineData("X is sign(-2r3)", "-1")]
    [InlineData("X is floor(7r2)", "3")]
    [InlineData("X is ceiling(7r2)", "4")]
    [InlineData("X is truncate(-7r2)", "-3")]
    [InlineData("X is round(7r2)", "4")]
    [InlineData("X is integer(7r2)", "4")]
    [InlineData("X is integer(-7r2)", "-4")]
    [InlineData("X is numerator(6r4)", "3")]
    [InlineData("X is denominator(6r4)", "2")]
    [InlineData("X is numerator(5)", "5")]
    [InlineData("X is denominator(5)", "1")]
    [InlineData("X is rational(0.5)", "1r2")]
    [InlineData("X is rationalize(0.1)", "1r10")]
    [InlineData("X is float(1r2)", "0.5")]
    [InlineData("X is min(1r3, 0)", "0")]
    [InlineData("X is max(1r3, 0)", "1r3")]
    [InlineData("X is 1r3 - 1r3", "0")]
    public void EvaluatesRationalsExactly(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(X)"));

    [Theory]
    [InlineData("1r3 =:= 1r3", "yes")]
    [InlineData("1r2 =:= 0.5", "yes")]
    [InlineData("1r2 < 1", "yes")]
    [InlineData("1r3 < 1r2", "yes")]
    [InlineData("2r3 > 1r2", "yes")]
    [InlineData("rational(1r3)", "yes")]
    [InlineData("rational(5)", "yes")]
    [InlineData("rational(0.5)", "no")]
    [InlineData("integer(1r3)", "no")]
    [InlineData("number(1r3)", "yes")]
    [InlineData("atomic(1r3)", "yes")]
    [InlineData("must_be(rational, 1r3)", "yes")]
    [InlineData("is_of_type(rational, 7)", "yes")]
    public void TestsAndComparesRationals(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void EqualRationalValuesAreIdenticalTerms() =>
        Assert.Equal("eq", PrologTestHost.RunGoal("X is 1 rdiv 3, Y = 1r3, ( X == Y -> write(eq) ; write(ne) )"));

    [Fact]
    public void RationalsRankWithIntegersInTheStandardOrder() =>
        Assert.Equal("[1r2,1,3r2,2,foo]", PrologTestHost.RunGoal("msort([1r2, 2, 1, 3r2, foo], L), write(L)"));

    [Fact]
    public void RationalsSurviveTheDynamicDatabaseAndCopying() =>
        Assert.Equal(
            "[22r7]",
            PrologTestHost.RunGoal(
                "X = 22r7, assertz(ratio(X)), findall(V, ratio(V), L), copy_term(L, L2), nb_setval(r, L2), nb_getval(r, Out), write(Out)"
            )
        );

    [Fact]
    public void FirstArgumentIndexingDistinguishesRationalValues() =>
        Assert.Equal(
            "b",
            PrologTestHost.Run(
                """
                p(1r3, a).
                p(1r4, b).
                :- initialization((X is 1 rdiv 4, p(X, R), write(R))).
                """
            )
        );

    [Theory]
    [InlineData("catch(X is 0.5 rdiv 2, error(E, _), true)", "type_error(rational,0.5)")]
    [InlineData("catch(X is 1 rdiv 0, error(E, _), true)", "evaluation_error(zero_divisor)")]
    [InlineData("catch(X is 1r2 >> 1, error(E, _), true)", "type_error(integer,1r2)")]
    [InlineData("catch(X is 1r2 mod 2, error(E, _), true)", "type_error(integer,1r2)")]
    [InlineData("catch(X is numerator(0.5), error(E, _), true)", "type_error(rational,0.5)")]
    public void GuardsTheNonRationalOperands(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, ( var(E) -> write(no_error) ; write(E) )"));

    [Fact]
    public void AtomNumberReadsTheRationalSpelling() =>
        Assert.Equal("22r7-23r7", PrologTestHost.RunGoal("atom_number('22r7', X), Y is X + 1r7, write(X-Y)"));

    [Fact]
    public void StrictModeKeepsIsoLexing()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p(1r3).", "strict.pl");

        Assert.False(loaded.Success);
    }

    [Fact]
    public void TheEmbeddingSurfaceMarshalsRationals()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText("half(X, Y) :- Y is X / 2.").Success);

        var host = new PrologHost(engine.Machine);
        PrologPredicate halving = host.Bind("half", 2);

        PrologValue[]? outputs = host.CallOnce(halving, PrologInput.Rational(1, 3), PrologInput.Output);

        Assert.NotNull(outputs);
        PrologRational result = Assert.IsType<PrologRational>(outputs[0]);
        Assert.Equal(new System.Numerics.BigInteger(1), result.Numerator);
        Assert.Equal(new System.Numerics.BigInteger(6), result.Denominator);
    }
}
