namespace DotProlog.Compiler.Tests;

/// <summary>ISO modes, type errors, and non-negative domains for atomic term processing.</summary>
public sealed class AtomicTermProcessingTests
{
    [Theory]
    [InlineData("atom_length(1, _)", "type_error(atom,1)")]
    [InlineData("atom_length(a, bad)", "type_error(integer,bad)")]
    [InlineData("atom_length(a, -1)", "domain_error(not_less_than_zero,-1)")]
    [InlineData("atom_concat(1, b, _)", "type_error(atom,1)")]
    [InlineData("atom_concat(a, 2, _)", "type_error(atom,2)")]
    [InlineData("atom_concat(a, b, 3)", "type_error(atom,3)")]
    [InlineData("atom_concat(_, b, _)", "instantiation_error")]
    [InlineData("atom_concat(a, _, _)", "instantiation_error")]
    [InlineData("sub_atom(1, _, _, _, _)", "type_error(atom,1)")]
    [InlineData("sub_atom(a, -1, _, _, _)", "domain_error(not_less_than_zero,-1)")]
    [InlineData("sub_atom(a, _, -1, _, _)", "domain_error(not_less_than_zero,-1)")]
    [InlineData("sub_atom(a, _, _, -1, _)", "domain_error(not_less_than_zero,-1)")]
    [InlineData("sub_atom(a, _, _, _, 1)", "type_error(atom,1)")]
    [InlineData("atom_chars(1.0, _)", "type_error(atom,1.0)")]
    [InlineData("atom_chars(_, atom)", "type_error(list,atom)")]
    [InlineData("atom_chars(_, [a|_])", "instantiation_error")]
    [InlineData("atom_chars(_, [ab])", "type_error(character,ab)")]
    [InlineData("atom_codes(1, _)", "type_error(atom,1)")]
    [InlineData("atom_codes(_, [a])", "type_error(integer,a)")]
    [InlineData("atom_codes(_, [-1])", "representation_error(character_code)")]
    [InlineData("number_chars(atom, _)", "type_error(number,atom)")]
    [InlineData("number_chars(_, atom)", "type_error(list,atom)")]
    [InlineData("number_chars(_, ['3',' '])", "syntax_error(illegal_number)")]
    [InlineData("number_codes(_, [a])", "type_error(integer,a)")]
    [InlineData("number_codes(_, [51,32])", "syntax_error(illegal_number)")]
    [InlineData("char_code(ab, _)", "type_error(character,ab)")]
    [InlineData("char_code(_, a)", "type_error(integer,a)")]
    public void ReportsIsoAtomicProcessingErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("atom_length(ab, 3)")]
    [InlineData("atom_concat(a, b, ac)")]
    [InlineData("sub_atom(ab, 0, 3, _, _)")]
    [InlineData("atom_chars(ab, [a,c])")]
    public void ValidButNonMatchingModesFail(string goal) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("number_chars")]
    [InlineData("number_codes")]
    public void SmallFloatTextRoundTrips(string predicate) =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"{predicate}(0.000000000001, Text), "
                    + $"{predicate}(Value, Text), "
                    + "Value == 0.000000000001, write(yes)"
            )
        );
}
