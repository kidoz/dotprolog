namespace DotProlog.Compiler.Tests;

/// <summary>
/// <c>bagof/3</c> and <c>setof/3</c>: grouping by the goal's free variables, and failing rather than
/// returning an empty bag.
/// </summary>
public sealed class BagofTests
{
    private const string Facts = """
        age(peter, 7).
        age(ann, 11).
        age(pat, 8).

        class(peter, a).
        class(ann, b).
        class(pat, a).

        """;

    private static string Run(string goal) => PrologTestHost.Run($"{Facts}:- initialization(({goal})).\n");

    [Fact]
    public void BagofBacktracksOverEachBindingOfAFreeVariable() =>
        Assert.Equal("[a-[peter,pat],b-[ann]]", Run("findall(C-L, bagof(N, class(N, C), L), R), write(R)"));

    [Fact]
    public void AQuantifiedVariableIsNotFree() => Assert.Equal("[peter,ann,pat]", Run("bagof(N, C^class(N, C), L), write(L)"));

    [Fact]
    public void SeveralQuantifiersNest() =>
        Assert.Equal("[peter,ann,pat]", Run("bagof(N, A^C^(class(N, C), age(N, A)), L), write(L)"));

    [Fact]
    public void QuantifyingATermQuantifiesItsVariables() =>
        Assert.Equal("[peter,ann,pat]", Run("bagof(N, f(C, A)^(class(N, C), age(N, A)), L), write(L)"));

    [Fact]
    public void SetofSortsAndRemovesDuplicates() =>
        Assert.Equal("[7-peter,8-pat,11-ann]", Run("setof(A-N, age(N, A), L), write(L)"));

    [Fact]
    public void SetofRemovesDuplicateSolutions() =>
        Assert.Equal("[a,b]", PrologTestHost.RunGoal("setof(X, member(X, [b, a, b, a]), L), write(L)"));

    [Fact]
    public void BagofKeepsDuplicatesAndSolutionOrder() =>
        Assert.Equal("[b,a,b]", PrologTestHost.RunGoal("bagof(X, member(X, [b, a, b]), L), write(L)"));

    [Theory]
    [InlineData("bagof(X, member(X, []), _)")]
    [InlineData("setof(X, member(X, []), _)")]
    [InlineData("bagof(X, (member(X, [1]), fail), _)")]
    public void FailsWhenTheGoalHasNoSolutions(string goal) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Fact]
    public void FindallReturnsAnEmptyListWhereBagofFails() =>
        Assert.Equal("[]", PrologTestHost.RunGoal("findall(X, member(X, []), L), write(L)"));

    [Fact]
    public void AVariableUnderNegationIsNotFree()
    {
        // Negation proves a goal and never binds anything, so Y cannot be a witness. Treating it as
        // one would put every solution in its own group, since each findall copy has a fresh Y.
        Assert.Equal("[1,2]", PrologTestHost.RunGoal("bagof(X, (member(X, [1, 2]), \\+ member(Y, [])), L), write(L)"));
    }

    [Fact]
    public void FreeVariablesInsideADisjunctionAreFound() =>
        Assert.Equal("[a-[peter,pat],b-[ann]]", Run("findall(C-L, bagof(N, (class(N, C) ; fail), L), R), write(R)"));

    [Fact]
    public void SeveralFreeVariablesGroupTogether() =>
        Assert.Equal(
            "[f(a,7)-[peter],f(a,8)-[pat],f(b,11)-[ann]]",
            Run("findall(f(C, A)-L, bagof(N, (class(N, C), age(N, A)), L), R), write(R)")
        );

    [Fact]
    public void ABoundWitnessSelectsOneGroup() => Assert.Equal("[peter,pat]", Run("C = a, bagof(N, class(N, C), L), write(L)"));

    [Fact]
    public void SetofGroupsLikeBagofDoes() =>
        Assert.Equal("[a-[pat,peter],b-[ann]]", Run("findall(C-L, setof(N, class(N, C), L), R), write(R)"));

    [Fact]
    public void TheTemplateNeedNotBeAVariable() =>
        Assert.Equal("[said(peter),said(ann),said(pat)]", Run("bagof(said(N), C^class(N, C), L), write(L)"));

    [Fact]
    public void CaretIsStillArithmeticPower() => Assert.Equal("8", PrologTestHost.RunGoal("X is 2 ^ 3, write(X)"));

    [Fact]
    public void CaretCalledAsAGoalRunsTheGoal() => Assert.Equal("yes", PrologTestHost.RunGoal("call(_^true), write(yes)"));

    [Fact]
    public void AnUninstantiatedGoalIsReported() =>
        Assert.Equal("instantiation_error", PrologTestHost.RunGoal("catch(bagof(_, _, _), error(E, _), write(E))"));
}
