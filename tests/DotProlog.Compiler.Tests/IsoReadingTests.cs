using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Negative numbers read as ISO requires in both modes. Where ISO and SWI-Prolog read other text
/// differently, strict ISO mode reads it the standard's way and <c>Modern</c> keeps SWI-Prolog's:
/// an atom that is an operator used as an operand, and a back-quoted string.
/// </summary>
public sealed class IsoReadingTests
{
    // 6.3.4.1: a - name token followed by a numeric literal is a negative number, after layout or a
    // comment too, and quoted or not.
    [Theory]
    [InlineData("integer(- 1)", "true")]
    [InlineData("integer('-'1)", "true")]
    [InlineData("integer('-' 1)", "true")]
    [InlineData("integer(- /* c */ 1)", "true")]
    [InlineData("X = - 1.5, float(X)", "true")]
    [InlineData("(- 1^2) == (-1)^2", "true")]
    [InlineData("integer(-1)", "true")]
    [InlineData("-(1) == - (1)", "true")]
    [InlineData("integer(+1)", "false")]
    public void AMinusNameBeforeANumberIsANegativeNumberInBothModes(string goal, string expected)
    {
        Assert.Equal(expected, Outcome(PrologLanguageMode.StrictIso, goal));
        Assert.Equal(expected, Outcome(PrologLanguageMode.Modern, goal));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void AMinusStillFormsANumberWhenItIsNoLongerAnOperator(PrologLanguageMode mode)
    {
        using var engine = new PrologEngine(mode) { Output = new StringWriter() };
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
        using var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
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
    public void TheBundledLibraryLoadsInStrictIso()
    {
        using var engine = new PrologEngine(PrologLanguageMode.StrictIso);
        Assert.True(engine.Query("atom_length(abc, 3)").Prove());
    }

    private static string Outcome(PrologLanguageMode mode, string goal)
    {
        using var engine = new PrologEngine(mode) { Output = new StringWriter() };
        return engine.Query(goal).Prove() ? "true" : "false";
    }

    private static string ReadOutcome(PrologLanguageMode mode, string text)
    {
        using var engine = new PrologEngine(mode) { Output = new StringWriter(), Input = new StringReader(text) };
        return engine
            .Query("catch((read(_), Outcome = read), error(syntax_error(_), _), Outcome = syntax_error), write(Outcome)")
            .Prove()
            ? ((StringWriter)engine.Output).ToString()
            : "failed";
    }
}
