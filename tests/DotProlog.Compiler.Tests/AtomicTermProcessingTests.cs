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
    [InlineData("atom_number('1.0e400', _)", "syntax_error(float_overflow)")]
    [InlineData("number_chars(_, ['1','.','0','e','4','0','0'])", "syntax_error(float_overflow)")]
    public void ReportsIsoAtomicProcessingErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    // Integers are unbounded: conversions past the fixnum range answer exact values.
    [Theory]
    [InlineData(
        "number_chars(N, ['1','0','0','0','0','0','0','0','0','0','0','0','0','0','0','0','0','0','0'])",
        "1000000000000000000"
    )]
    [InlineData("number_codes(N, [45,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57])", "-9999999999999999999")]
    [InlineData("atom_number('1000000000000000000', N)", "1000000000000000000")]
    [InlineData("atom_number('0x10000000000000000', N)", "18446744073709551616")]
    [InlineData("atom_number('-0x10000000000000000', N)", "-18446744073709551616")]
    public void ConvertsIntegersBeyondTheFixnumRange(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(N)"));

    [Theory]
    [InlineData("atom_length(ab, 3)")]
    [InlineData("atom_concat(a, b, ac)")]
    [InlineData("sub_atom(ab, 0, 3, _, _)")]
    [InlineData("atom_chars(ab, [a,c])")]
    [InlineData("atom_chars(ab, [_,_,_])")]
    [InlineData("number_codes(33, [0'3])")]
    public void ValidButNonMatchingModesFail(string goal) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("atom_chars(abc, [X, Y, Z]), atom_chars(A, [X, Y, Z]), write(A)", "abc")]
    [InlineData("atom_codes(abc, [X | _]), write(X)", "97")]
    [InlineData("number_chars(33, [X, _]), write(X)", "3")]
    [InlineData("number_codes(33, L), write(L)", "[51,51]")]
    public void ABoundFirstArgumentConvertsAndUnifiesWithTheList(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("number_chars")]
    [InlineData("number_codes")]
    public void SmallFloatTextRoundTrips(string predicate) =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"{predicate}(0.000000000001, Text), " + $"{predicate}(Value, Text), " + "Value == 0.000000000001, write(yes)"
            )
        );
}
