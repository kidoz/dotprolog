using System.Text.RegularExpressions;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// <c>length/2</c> against the ISO conformity table at
/// https://www.complang.tuwien.ac.at/ulrich/iso-prolog/length, whose case numbers the comments cite.
/// The length is checked before the list is walked, the walk fails rather than loops on a cyclic
/// list, and a list with too many elements for a bound length fails at once. The cases that need
/// <c>freeze/2</c> are left out, since attributed variables are not supported.
/// </summary>
public sealed class LengthConformityTests
{
    [Theory]
    [InlineData("length([_|L], 0)")] // 3
    [InlineData("length(2, 0)")] // 4
    [InlineData("length([_|2], 0)")] // 5
    [InlineData("length([_|2], N)")] // 6
    [InlineData("length([_|2], 2)")] // 7
    [InlineData("N is 2^52, length([], N)")] // 17
    [InlineData("L = [a|L], length(L, N)")] // 26
    [InlineData("L = [a|L], length(L, 0)")] // 27
    [InlineData("L = [a|L], length(L, 7)")] // 28
    public void Fails(string goal) => Assert.Equal("false", Outcome(goal));

    [Theory]
    [InlineData("length(L, -1)", "domain_error(not_less_than_zero,-1)")] // 8
    [InlineData("length([], -1)", "domain_error(not_less_than_zero,-1)")] // 9
    [InlineData("length(a, -1)", "domain_error(not_less_than_zero,-1)")] // 10
    [InlineData("length([], -0.1)", "type_error(integer,-0.1)")] // 11
    [InlineData("length(L, -0.1)", "type_error(integer,-0.1)")] // 12
    [InlineData("length([a], 1.0)", "type_error(integer,1.0)")] // 13
    [InlineData("length(L, 1.0)", "type_error(integer,1.0)")] // 14
    [InlineData("length(L, 1.1)", "type_error(integer,1.1)")] // 15
    [InlineData("length([], 0+0)", "type_error(integer,0+0)")] // 18
    [InlineData("length([], -_)", "type_error(integer,-A)")] // 19
    [InlineData("length([a], -_)", "type_error(integer,-A)")] // 20
    [InlineData("length([a, b|X], X)", "resource_error(finite_memory)")] // 21
    [InlineData("length(L, L)", "resource_error(finite_memory)")] // 22
    [InlineData("L = [_|_], length(L, L)", "type_error(integer,[A|B])")] // 23
    [InlineData("L = [_], length(L, L)", "type_error(integer,[A])")] // 24
    [InlineData("L = [1], length(L, L)", "type_error(integer,[1])")] // 25
    public void ReportsTheErrorsTheTableExpects(string goal, string expected) => Assert.Equal(expected, Outcome(goal));

    [Theory]
    [InlineData("length(L, 0), write(L)", "[]")] // 2
    [InlineData("length(L, N), N >= 2, !, write(L/N)", "[_,_]/2")] // 1
    [InlineData("length([a, b|L], N), N >= 4, !, write(L/N)", "[_,_]/4")] // 32
    public void Answers(string goal, string expected) =>
        Assert.Equal(expected, Regex.Replace(PrologTestHost.RunGoal(goal), "_G[0-9]+", "_"));

    [Fact]
    public void AClosedListOrABoundLengthLeavesNoChoicePoint() =>
        Assert.Equal(
            "same same",
            PrologTestHost.RunGoal(
                "'$choice_points'(A0), length([a, b], _), '$choice_points'(A1), "
                    + "'$choice_points'(B0), length(_, 2), '$choice_points'(B1), "
                    + "( A0 == A1 -> write(same) ; write(different) ), write(' '), "
                    + "( B0 == B1 -> write(same) ; write(different) )"
            )
        );

    private static string Outcome(string goal) =>
        PrologTestHost.RunGoal(
            $"catch((({goal}) -> Outcome_ = true ; Outcome_ = false), error(Error_, _), Outcome_ = Error_), "
                + "copy_term(Outcome_, Shown_), numbervars(Shown_, 0, _), writeq(Shown_)"
        );
}
