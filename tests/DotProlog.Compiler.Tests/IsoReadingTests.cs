using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Where ISO and SWI-Prolog read the same text differently, strict ISO mode reads it the standard's
/// way and <c>Modern</c> keeps SWI-Prolog's: a <c>-</c> name before a numeric literal, an atom that
/// is an operator used as an operand, and a back-quoted string.
/// </summary>
public sealed class IsoReadingTests
{
    // 6.3.4.1: a - name token followed by a numeric literal is a negative number, after layout or a
    // comment too, and quoted or not. SWI-Prolog reads only an unquoted - directly before the digits.
    [Theory]
    [InlineData("integer(- 1)", "true", "false")]
    [InlineData("integer('-'1)", "true", "false")]
    [InlineData("integer('-' 1)", "true", "false")]
    [InlineData("integer(- /* c */ 1)", "true", "false")]
    [InlineData("X = - 1.5, float(X)", "true", "false")]
    [InlineData("(- 1^2) == (-1)^2", "true", "false")]
    [InlineData("integer(-1)", "true", "true")]
    [InlineData("-(1) == - (1)", "true", "true")]
    [InlineData("integer(+1)", "false", "false")]
    public void AMinusNameBeforeANumberIsANegativeNumberInStrictIso(string goal, string strict, string modern)
    {
        Assert.Equal(strict, Outcome(PrologLanguageMode.StrictIso, goal));
        Assert.Equal(modern, Outcome(PrologLanguageMode.Modern, goal));
    }

    [Fact]
    public void AMinusStillFormsANumberWhenItIsNoLongerAnOperator()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- op(0, fy, -).\nminus(X) :- X = - 1.\n").Success);
        Assert.True(engine.Query("minus(X), integer(X), X =:= -1").Prove());
    }

    // 6.3.1.3: an atom that is an operator has priority 1201 as an operand, so it needs brackets there,
    // while a whole term, an argument, or a list element may be one bare.
    [Theory]
    [InlineData("X = - .")]
    [InlineData("- = - .")]
    [InlineData("* = * .")]
    [InlineData(@"X = '\\' .")]
    [InlineData("X = (- -) .")]
    [InlineData("X = (-;-) .")]
    [InlineData("X = (-, 1) .")]
    [InlineData("X = {- = a} .")]
    public void AnOperatorAtomIsNoBareOperandInStrictIso(string text)
    {
        Assert.Equal("syntax_error", ReadOutcome(PrologLanguageMode.StrictIso, text));
        Assert.NotEqual("syntax_error", ReadOutcome(PrologLanguageMode.Modern, text));
    }

    [Theory]
    [InlineData("X = (-) .")]
    [InlineData("X = f(-, :-, *) .")]
    [InlineData("X = [-, :-|*] .")]
    [InlineData("X = - (-) .")]
    [InlineData("X = {-} .")]
    [InlineData("- .")]
    [InlineData("X = (a :- b, c) .")]
    public void AnOperatorAtomStandsBareWhereAWholeTermOrArgumentDoes(string text) =>
        Assert.Equal("read", ReadOutcome(PrologLanguageMode.StrictIso, text));

    [Fact]
    public void PrefixAndPostfixOperatorsWithNoOperandAreSyntaxErrorsInStrictIso()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- op(9, fy, f), op(9, yf, f), op(9, fy, p), op(9, yfx, p).\n").Success);

        foreach (var text in new[] { "f f .", "1 p p p 2 ." })
        {
            engine.Input = new StringReader(text);
            Assert.True(engine.Query("catch((read(_), fail), error(syntax_error(_), _), true)").Prove(), text);
        }

        engine.Input = new StringReader("f f 0 .");
        Assert.True(engine.Query("read(T), T == f(f(0))").Prove());
    }

    [Fact]
    public void ABackQuotedStringIsNoTermInStrictIso()
    {
        Assert.Equal("syntax_error", ReadOutcome(PrologLanguageMode.StrictIso, "X = `a` ."));
        Assert.Equal("read", ReadOutcome(PrologLanguageMode.Modern, "X = `a` ."));
    }

    // The bundled library uses bare operator atoms as operands, and is read as Modern reads it.
    [Fact]
    public void TheBundledLibraryLoadsInStrictIso() =>
        Assert.True(new PrologEngine(PrologLanguageMode.StrictIso).Query("atom_length(abc, 3)").Prove());

    private static string Outcome(PrologLanguageMode mode, string goal)
    {
        var engine = new PrologEngine(mode) { Output = new StringWriter() };
        return engine.Query(goal).Prove() ? "true" : "false";
    }

    private static string ReadOutcome(PrologLanguageMode mode, string text)
    {
        var engine = new PrologEngine(mode) { Output = new StringWriter(), Input = new StringReader(text) };
        return engine
            .Query("catch((read(_), Outcome = read), error(syntax_error(_), _), Outcome = syntax_error), write(Outcome)")
            .Prove()
            ? ((StringWriter)engine.Output).ToString()
            : "failed";
    }
}
