using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The shared text reader behind the SWI-aligned extension predicates: double-quoted text under the
/// default <c>chars</c> reading, the readings of <c>[]</c>, and the strict mode
/// left untouched. Agreement with SWI-Prolog itself is checked by the differential corpus.
/// </summary>
public sealed class TextAcceptanceTests
{
    [Theory]
    [InlineData("string_length(\"abc\", N), write(N)", "3")]
    [InlineData("split_string(\"a,b\", \",\", \"\", P), writeq(P)", "[\"a\",\"b\"]")]
    [InlineData("string_code(1, \"abc\", C), write(C)", "97")]
    [InlineData("atom_string(A, \"xy\"), writeq(A)", "xy")]
    [InlineData("text_to_string(\"xy\", S), writeq(S)", "\"xy\"")]
    [InlineData("format(\"~s|~a\", [\"ab\", cd])", "ab|cd")]
    public void DoubleQuotedTextIsTextUnderTheCharsDefault(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("string_length(\"\", N), write(N)", "0")]
    [InlineData("string_length([], N), write(N)", "0")]
    [InlineData("split_string(\"\", \",\", \"\", P), writeq(P)", "[\"\"]")]
    [InlineData("format(\"\", []), write(done)", "done")]
    [InlineData("atom_string([], S), writeq(S)", "\"\"")]
    public void EmptyListIsEmptyTextWhereListsAreText(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("string_concat([], a, S), writeq(S)", "\"[]a\"")]
    [InlineData("upcase_atom([], U), writeq(U)", "[]")]
    public void EmptyListIsTheAtomWhereTermsAreParsedOrListsAreNotText(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("string_upper(abc, 'ABC')")]
    [InlineData("atom_string(abc, \"abc\")")]
    [InlineData("string_chars(abc, \"abc\")")]
    [InlineData("text_to_string(\"ab\", ab)")]
    public void BoundOutputsCompareByText(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("catch(format(\"~a\", [f(x)]), error(E, _), true), writeq(E)", "format_argument_type(a,f(x))")]
    [InlineData("catch(format(\"~s\", [f(x)]), error(E, _), true), writeq(E)", "format_argument_type(s,f(x))")]
    [InlineData("catch(format(f(x), []), error(E, _), true), writeq(E)", "type_error(text,f(x))")]
    [InlineData("catch(format([foo], []), error(E, _), true), writeq(E)", "type_error(text,[foo])")]
    public void RejectedTextRaisesSwisError(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("atom_length(\"abc\", _)", "type_error(atom,[a,b,c])")]
    [InlineData("sub_atom(\"abc\", 0, 1, _, _)", "type_error(atom,[a,b,c])")]
    public void IsoPredicatesKeepIsoAcceptance(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), true), writeq(E)"));

    [Fact]
    public void StrictModeDoesNotOfferTheStringLibrary()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };

        Assert.Equal(
            RunResult.Success,
            engine.RunGoal(
                "catch(call(string_length, abc, _), error(E, _), true), E = permission_error(access, implementation_specific_feature, string_length/2)",
                out _
            )
        );
    }
}
