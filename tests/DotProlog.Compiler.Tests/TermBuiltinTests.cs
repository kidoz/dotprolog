using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Type tests, standard-order comparison, and term construction and inspection.</summary>
public sealed class TermBuiltinTests
{
    [Theory]
    [InlineData("var(X)")]
    [InlineData("nonvar(a)")]
    [InlineData("atom(foo)")]
    [InlineData("atom([])")]
    [InlineData("integer(1)")]
    [InlineData("float(1.5)")]
    [InlineData("number(1)")]
    [InlineData("number(1.5)")]
    [InlineData("compound(f(a))")]
    [InlineData("compound([a])")]
    [InlineData("atomic(a)")]
    [InlineData("atomic(1)")]
    [InlineData("callable(foo)")]
    [InlineData("callable(f(x))")]
    [InlineData("is_list([a,b])")]
    [InlineData("is_list([])")]
    [InlineData("ground(f(a,b))")]
    public void TypeTestsSucceedWhenTheyHold(string goal)
    {
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));
    }

    [Theory]
    [InlineData("var(a)")]
    [InlineData("nonvar(X)")]
    [InlineData("atom(1)")]
    [InlineData("atom(f(a))")]
    [InlineData("integer(1.5)")]
    [InlineData("float(1)")]
    [InlineData("compound(a)")]
    [InlineData("atomic(f(a))")]
    [InlineData("callable(1)")]
    [InlineData("is_list([a|T])")]
    [InlineData("ground(f(a,Y))")]
    public void TypeTestsFailWhenTheyDoNotHold(string goal)
    {
        Assert.Equal("yes", PrologTestHost.RunGoal($"\\+ {goal}, write(yes)"));
    }

    [Theory]
    [InlineData("a == a")]
    [InlineData("f(X) == f(X)")]
    [InlineData("a \\== b")]
    [InlineData("X \\== Y")]
    [InlineData("1 @< a")]
    [InlineData("a @< f(a)")]
    [InlineData("f(a) @< f(b)")]
    [InlineData("f(a) @< g(a,b)")]
    [InlineData("1 @< 2")]
    [InlineData("1.0 @< 1")]
    [InlineData("a @=< a")]
    [InlineData("b @> a")]
    public void StandardOrderComparisonsHold(string goal)
    {
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));
    }

    [Fact]
    public void UnboundVariablesPrecedeEveryOtherKindOfTerm()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("X @< 1, X @< a, X @< f(a), write(yes)"));
    }

    [Theory]
    [InlineData("compare(R, a, b)", "<")]
    [InlineData("compare(R, b, a)", ">")]
    [InlineData("compare(R, a, a)", "=")]
    public void CompareReportsTheOrder(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(R)"));
    }

    [Theory]
    [InlineData("compare(1, a, b)", "type_error(atom,1)")]
    [InlineData("compare(foo, a, b)", "domain_error(order,foo)")]
    public void CompareRejectsInvalidOrderArguments(string goal, string expected)
    {
        ArgumentNullException.ThrowIfNull(goal);

        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
    }

    [Fact]
    public void NotUnifiableLeavesNoBindingsBehind()
    {
        // X \= a must fail because they unify, and X must still be unbound afterwards.
        Assert.Equal("unbound", PrologTestHost.RunGoal("\\+ (X \\= a), var(X), write(unbound)"));
    }

    [Fact]
    public void NotUnifiableSucceedsForTermsThatCannotMatch()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("f(a) \\= f(b), write(yes)"));
    }

    [Theory]
    [InlineData("unify_with_occurs_check(X, f(a)), X == f(a)")]
    [InlineData("unify_with_occurs_check(X, Y), X == Y")]
    [InlineData("\\+ unify_with_occurs_check(X, f(X)), var(X)")]
    [InlineData("\\+ unify_with_occurs_check(f(X, X), f(a, b)), var(X)")]
    [InlineData("(unify_with_occurs_check(X, a), fail ; var(X))")]
    [InlineData("X = f(X), nonvar(X)")]
    public void OccursCheckUnificationHasIsoBindingSemantics(string goal)
    {
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));
    }

    [Theory]
    [InlineData("functor(f(a,b), N, A)", "f 2")]
    [InlineData("functor(foo, N, A)", "foo 0")]
    [InlineData("functor(42, N, A)", "42 0")]
    [InlineData("functor([a], N, A)", ". 2")]
    public void FunctorDecomposesATerm(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(N), write(' '), write(A)"));
    }

    [Fact]
    public void FunctorBuildsATermFromANameAndArity()
    {
        Assert.Equal("point(_", PrologTestHost.RunGoal("functor(T, point, 2), write(T)")[..7]);
    }

    [Fact]
    public void FunctorBuildsAnAtomForArityZero()
    {
        Assert.Equal("hello", PrologTestHost.RunGoal("functor(T, hello, 0), write(T)"));
    }

    [Theory]
    [InlineData("arg(1, f(a,b), X)", "a")]
    [InlineData("arg(2, f(a,b), X)", "b")]
    public void ArgSelectsAnArgument(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(X)"));
    }

    [Fact]
    public void ArgFailsOutsideTheArgumentRange()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("\\+ arg(3, f(a,b), _), write(yes)"));
    }

    [Theory]
    [InlineData("f(a,b) =.. L", "[f,a,b]")]
    [InlineData("foo =.. L", "[foo]")]
    [InlineData("42 =.. L", "[42]")]
    public void UnivDecomposesATerm(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(L)"));
    }

    [Theory]
    [InlineData("T =.. [f,a,b]", "f(a,b)")]
    [InlineData("T =.. [foo]", "foo")]
    [InlineData("T =.. [42]", "42")]
    public void UnivBuildsATerm(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(T)"));
    }

    [Fact]
    public void UnivRoundTripsATerm()
    {
        Assert.Equal("g(1,h(2))", PrologTestHost.RunGoal("g(1, h(2)) =.. L, T =.. L, write(T)"));
    }

    [Fact]
    public void UnivOnAnEmptyListIsADomainError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(T =.. []).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("domain_error(non_empty_list", exception.Message, StringComparison.Ordinal);
    }
}
