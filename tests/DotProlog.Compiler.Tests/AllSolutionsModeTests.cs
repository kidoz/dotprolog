namespace DotProlog.Compiler.Tests;

/// <summary>ISO callable and partial-list modes shared by the three all-solutions predicates.</summary>
public sealed class AllSolutionsModeTests
{
    [Theory]
    [InlineData("findall(_, true, atom)", "type_error(list,atom)")]
    [InlineData("findall(_, true, [a|tail])", "type_error(list,[a|tail])")]
    [InlineData("bagof(_, true, atom)", "type_error(list,atom)")]
    [InlineData("bagof(_, true, [a|tail])", "type_error(list,[a|tail])")]
    [InlineData("setof(_, true, atom)", "type_error(list,atom)")]
    [InlineData("setof(_, true, [a|tail])", "type_error(list,[a|tail])")]
    [InlineData("findall(_, fail, atom)", "type_error(list,atom)")]
    [InlineData("bagof(_, fail, atom)", "type_error(list,atom)")]
    [InlineData("setof(_, fail, atom)", "type_error(list,atom)")]
    [InlineData("findall(_, _, atom)", "instantiation_error")]
    [InlineData("bagof(_, _, atom)", "instantiation_error")]
    [InlineData("setof(_, _, atom)", "instantiation_error")]
    [InlineData("findall(_, (fail, 4), atom)", "type_error(callable,(fail,4))")]
    public void ReportsIsoAllSolutionsErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("findall(X, member(X, [a,b]), [a|Tail]), Tail == [b]")]
    [InlineData("bagof(X, member(X, [a,b]), [a|Tail]), Tail == [b]")]
    [InlineData("setof(X, member(X, [b,a]), [a|Tail]), Tail == [b]")]
    public void AcceptsPartialResultLists(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("findall(X, member(X, [a,b]), [a])")]
    [InlineData("bagof(X, member(X, [a,b]), [a])")]
    [InlineData("setof(X, member(X, [b,a]), [a])")]
    public void ValidButNonMatchingResultListsFail(string goal) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));
}
