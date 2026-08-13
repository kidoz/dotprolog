using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Unbounded integer arithmetic and the big-integer term tier: fixnum operations
/// promote past the 60-bit range, results normalize back when they fit, and a big integer behaves
/// as an ordinary atomic term through unification, ordering, indexing, and the database.
/// </summary>
public sealed class UnboundedIntegerTests
{
    [Theory]
    [InlineData("X is 2 ^ 100", "1267650600228229401496703205376")]
    [InlineData("X is 10 ^ 30 + 10 ^ 30", "2000000000000000000000000000000")]
    [InlineData("X is max_tagged_integer + 1", "576460752303423488")]
    [InlineData("X is min_tagged_integer - 1", "-576460752303423489")]
    [InlineData("X is -(10 ^ 30)", "-1000000000000000000000000000000")]
    [InlineData("X is 999999999999 * 999999999999", "999999999998000000000001")]
    [InlineData("X is abs(-(10 ^ 30))", "1000000000000000000000000000000")]
    [InlineData("X is sign(-(10 ^ 30))", "-1")]
    [InlineData("X is 1 << 100", "1267650600228229401496703205376")]
    [InlineData("X is (10 ^ 40) >> 100", "7888609052")]
    [InlineData("X is (10 ^ 30) // (10 ^ 10)", "100000000000000000000")]
    [InlineData("X is (10 ^ 30) mod 7", "1")]
    [InlineData("X is (-(10 ^ 30)) mod 7", "6")]
    [InlineData("X is (-(10 ^ 30)) rem 7", "-1")]
    [InlineData("X is (-(10 ^ 30)) div 7", "-142857142857142857142857142858")]
    [InlineData("X is \\ (10 ^ 30)", "-1000000000000000000000000000001")]
    [InlineData("X is (10 ^ 30) /\\ 6", "0")]
    [InlineData("X is (10 ^ 30) \\/ 1", "1000000000000000000000000000001")]
    [InlineData("X is xor(10 ^ 30, 10 ^ 30)", "0")]
    [InlineData("X is min(10 ^ 30, 5)", "5")]
    [InlineData("X is max(10 ^ 30, 5)", "1000000000000000000000000000000")]
    [InlineData("X is integer(1.0e21)", "1000000000000000000000")]
    [InlineData("X is truncate(1.0e21)", "1000000000000000000000")]
    public void PromotesAndComputesExactly(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(X)"));

    // A result that re-enters the fixnum range leaves no big representation behind: the
    // canonicalization invariant that keeps cell equality usable as value identity.
    [Theory]
    [InlineData("X is 2 ^ 100 - 2 ^ 100 + 7", "7")]
    [InlineData("X is (2 ^ 100) // (2 ^ 90)", "1024")]
    [InlineData("X is (10 ^ 30) - (10 ^ 30)", "0")]
    [InlineData("X is max_tagged_integer + 1 - 1", "576460752303423487")]
    public void DemotesResultsThatFitTheFixnumRange(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, ( integer(X) -> write(X) ; write(not_integer) )"));

    [Theory]
    [InlineData("integer(123456789012345678901234567890)", "yes")]
    [InlineData("number(123456789012345678901234567890)", "yes")]
    [InlineData("atomic(123456789012345678901234567890)", "yes")]
    [InlineData("float(123456789012345678901234567890)", "no")]
    [InlineData("compound(123456789012345678901234567890)", "no")]
    public void TypeTestsTreatBigIntegersAsIntegers(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void EqualBigValuesAreIdenticalTerms() =>
        Assert.Equal(
            "eq",
            PrologTestHost.RunGoal("X is 10 ^ 30, Y = 1000000000000000000000000000000, ( X == Y -> write(eq) ; write(ne) )")
        );

    [Fact]
    public void BigIntegersRankWithIntegersInTheStandardOrder() =>
        Assert.Equal(
            "[1.5,2,1000000000000000000000000000000,foo]",
            PrologTestHost.RunGoal("msort([foo, 1000000000000000000000000000000, 2, 1.5], L), write(L)")
        );

    [Theory]
    [InlineData("10 ^ 30 > 10 ^ 29", "yes")]
    [InlineData("10 ^ 30 =:= 10 ^ 30", "yes")]
    [InlineData("-(10 ^ 30) < 3", "yes")]
    [InlineData("10 ^ 30 > 1.0e25", "yes")]
    [InlineData("10 ^ 30 < inf", "yes")]
    public void ComparesAcrossRepresentations(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void FirstArgumentIndexingDistinguishesBigValues() =>
        Assert.Equal(
            "b",
            PrologTestHost.Run(
                """
                p(100000000000000000000000000001, a).
                p(100000000000000000000000000002, b).
                :- initialization((X is 10 ^ 29 + 2, p(X, R), write(R))).
                """
            )
        );

    [Fact]
    public void BigIntegersSurviveTheDynamicDatabaseAndFindall() =>
        Assert.Equal(
            "[1000000000000000000000000000000]",
            PrologTestHost.RunGoal("X is 10 ^ 30, assertz(fact(X)), findall(V, fact(V), L), retract(fact(X)), write(L)")
        );

    [Fact]
    public void BigIntegersSurviveBacktracking() =>
        Assert.Equal(
            "1000000000000000000000000000000",
            PrologTestHost.RunGoal("( X is 10 ^ 30, fail ; true ), Y is 10 ^ 30, write(Y)")
        );

    [Theory]
    [InlineData("succ(999999999999999999999999999999, X)", "1000000000000000000000000000000")]
    [InlineData("succ(X, 1000000000000000000000000000000)", "999999999999999999999999999999")]
    [InlineData("plus(999999999999999999999999999999, 1, X)", "1000000000000000000000000000000")]
    [InlineData("plus(1, X, 1000000000000000000000000000000)", "999999999999999999999999999999")]
    [InlineData(
        "between(999999999999999999999999999998, 1000000000000000000000000000000, X), X > 999999999999999999999999999998, !",
        "999999999999999999999999999999"
    )]
    [InlineData("between(1, 1000000000000000000000000000000, X), X > 2, !", "3")]
    public void ArithmeticHelpersHandleBigValues(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(X)"));

    [Theory]
    [InlineData("catch(X is 2 ^ (2 ^ 40), error(E, _), true)", "resource_error(memory)")]
    [InlineData("catch(X is 1 << (2 ^ 40), error(E, _), true)", "resource_error(memory)")]
    [InlineData("catch(X is float(10 ^ 400), error(E, _), true)", "evaluation_error(float_overflow)")]
    [InlineData("catch(X is floor(10 ^ 30), error(E, _), true)", "type_error(float,1000000000000000000000000000000)")]
    [InlineData("catch(X is (10 ^ 30) ^ (-1), error(E, _), true)", "type_error(float,1000000000000000000000000000000)")]
    [InlineData("catch(X is integer(inf), error(E, _), true)", "evaluation_error(undefined)")]
    public void GuardsTheImpossibleResults(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, ( var(E) -> write(no_error) ; write(E) )"));

    [Theory]
    [InlineData("format('~d', [123456789012345678901234567890])", "123456789012345678901234567890")]
    [InlineData("format('~2d', [123456789012345678901234567890])", "1234567890123456789012345678.90")]
    [InlineData("format('~16r', [123456789012345678901234567890])", "18ee90ff6c373e0ee4e3f0ad2")]
    [InlineData("format('~w', [123456789012345678901234567890])", "123456789012345678901234567890")]
    [InlineData("format('~q', [-123456789012345678901234567890])", "-123456789012345678901234567890")]
    public void FormatsBigIntegers(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("atom_length(abc, 123456789012345678901234567890)", "no")]
    [InlineData("arg(123456789012345678901234567890, f(a, b), _)", "no")]
    [InlineData("string_code(123456789012345678901234567890, abc, _)", "no")]
    public void StructurallyBoundedDomainsFailForBigIndexes(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void AFunctorArityCannotBeBig() =>
        Assert.Equal(
            "representation_error(max_arity)",
            PrologTestHost.RunGoal("catch(functor(_, foo, 123456789012345678901234567890), error(E, _), true), write(E)")
        );

    [Fact]
    public void ACharCodeCannotBeBig() =>
        Assert.Equal(
            "representation_error(character_code)",
            PrologTestHost.RunGoal("catch(char_code(_, 123456789012345678901234567890), error(E, _), true), write(E)")
        );

    [Fact]
    public void TheBoundedFlagIsFalse() =>
        Assert.Equal("false", PrologTestHost.RunGoal("current_prolog_flag(bounded, B), write(B)"));

    [Fact]
    public void CopyTermAndGlobalVariablesPreserveBigValues() =>
        Assert.Equal(
            "1000000000000000000000000000000",
            PrologTestHost.RunGoal("X is 10 ^ 30, copy_term(f(X), f(Y)), nb_setval(big, Y), nb_getval(big, Z), write(Z)")
        );

    [Fact]
    public void TheEmbeddingSurfaceMarshalsBigIntegers()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText("double(X, Y) :- Y is X * 2.").Success);

        var host = new PrologHost(engine.Machine);
        PrologPredicate doubling = host.Bind("double", 2);

        var big = System.Numerics.BigInteger.Parse("123456789012345678901234567890");
        PrologValue[]? outputs = host.CallOnce(doubling, PrologInput.Big(big), PrologInput.Output);

        Assert.NotNull(outputs);
        Assert.Equal(big * 2, PrologMarshal.ToBigInteger(outputs[0]));
    }
}
