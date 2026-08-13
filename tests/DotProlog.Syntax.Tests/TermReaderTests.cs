using System.Globalization;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Syntax.Tests;

public sealed class TermReaderTests
{
    private static string Canonical(SyntaxTerm term) =>
        term switch
        {
            AtomTerm atom => atom.Name,
            IntegerTerm integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            FloatTerm number => number.Value.ToString("R", CultureInfo.InvariantCulture),
            VariableTerm variable => variable.Name,
            StringTerm text => $"\"{text.Value}\"",
            CompoundTerm compound => $"{compound.Name}({string.Join(",", compound.Arguments.Select(Canonical))})",
            _ => throw new InvalidOperationException($"Unhandled term {term.GetType().Name}."),
        };

    private static string ReadSingle(string text)
    {
        ParseResult result = TermReader.ReadProgram(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return Canonical(Assert.Single(result.Clauses));
    }

    [Fact]
    public void ReadsFactWithQuotedAtom()
    {
        Assert.Equal("greeting(Hello! World!)", ReadSingle("greeting('Hello! World!')."));
    }

    [Fact]
    public void ReadsBackquotedAtom()
    {
        Assert.Equal("greeting(Hello! World!)", ReadSingle("greeting(`Hello! World!`)."));
    }

    [Fact]
    public void ReadsRuleWithConjunctiveBody()
    {
        Assert.Equal(":-(main,,(write(hi),nl))", ReadSingle("main :- write(hi), nl."));
    }

    [Theory]
    [InlineData("X is 1 + 2 * 3.", "is(X,+(1,*(2,3)))")]
    [InlineData("X is 1 - 2 - 3.", "is(X,-(-(1,2),3))")]
    [InlineData("X is 2 ^ 3 ^ 2.", "is(X,^(2,^(3,2)))")]
    [InlineData("a :- b ; c.", ":-(a,;(b,c))")]
    [InlineData("a :- b -> c ; d.", ":-(a,;(->(b,c),d))")]
    [InlineData("a :- \\+ b.", ":-(a,\\+(b))")]
    public void AppliesOperatorPrioritiesAndAssociativity(string source, string expected)
    {
        Assert.Equal(expected, ReadSingle(source));
    }

    [Theory]
    [InlineData("p([]).", "p([])")]
    [InlineData("p([a]).", "p(.(a,[]))")]
    [InlineData("p([a,b]).", "p(.(a,.(b,[])))")]
    [InlineData("p([a,b|T]).", "p(.(a,.(b,T)))")]
    public void ReadsListsAsRightNestedPairs(string source, string expected)
    {
        Assert.Equal(expected, ReadSingle(source));
    }

    [Fact]
    public void RejectsChainedNonAssociativeOperator()
    {
        // '**' is xfx, so neither argument may itself be a '**' term.
        ParseResult result = TermReader.ReadProgram("X is 2 ** 3 ** 2.");

        Assert.False(result.Success);
    }

    [Fact]
    public void ReadsCurlyTermAsCompound()
    {
        Assert.Equal("p({}(,(a,b)))", ReadSingle("p({a, b})."));
    }

    [Fact]
    public void TreatsSignBeforeLiteralAsPartOfTheNumber()
    {
        Assert.Equal("p(-1,-2.5)", ReadSingle("p(-1, -2.5)."));
    }

    [Fact]
    public void TreatsSignBeforeNonLiteralAsPrefixOperator()
    {
        Assert.Equal("p(-(a))", ReadSingle("p(- a)."));
    }

    [Fact]
    public void CommaSeparatesArgumentsRatherThanOperating()
    {
        Assert.Equal("p(a,b)", ReadSingle("p(a, b)."));
        Assert.Equal("p(,(a,b))", ReadSingle("p((a, b))."));
    }

    [Theory]
    [InlineData("left 'is' right.", "is(left,right)")]
    [InlineData("left `is` right.", "is(left,right)")]
    [InlineData("'dynamic' predicate.", "dynamic(predicate)")]
    [InlineData("p('+', q).", "p(+,q)")]
    public void QuotedOperatorNamesRetainTheirOperatorMeaning(string source, string expected)
    {
        Assert.Equal(expected, ReadSingle(source));
    }

    [Fact]
    public void RecoversAtTheNextClauseAfterAnError()
    {
        ParseResult result = TermReader.ReadProgram("broken(a b).\ngood(c).");

        Assert.False(result.Success);
        Assert.Equal(DiagnosticIds.UnexpectedToken, result.Diagnostics[0].Id);
        Assert.Equal("good(c)", Canonical(Assert.Single(result.Clauses)));
    }

    [Fact]
    public void ReportsAMissingClauseTerminator()
    {
        ParseResult result = TermReader.ReadProgram("a\nb.");

        Assert.Equal(DiagnosticIds.MissingEndToken, Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void DiagnosticCarriesLineAndColumn()
    {
        ParseResult result = TermReader.ReadProgram("p(a).\nq(b c).", "test.pl");

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Span.Line);
        Assert.Equal("test.pl", diagnostic.FileName);
        Assert.StartsWith("test.pl(2,", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTermAcceptsAGoalWithoutATerminator()
    {
        ParseResult result = TermReader.ReadTerm("write(hi), nl");

        Assert.True(result.Success);
        Assert.Equal(",(write(hi),nl)", Canonical(Assert.Single(result.Clauses)));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("1e2")]
    public void ReadTermRejectsTrailingTokens(string source)
    {
        ParseResult result = TermReader.ReadTerm(source);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticIds.UnexpectedToken, Assert.Single(result.Diagnostics).Id);
    }

    // Integers are unbounded: a literal past the long range parses to a
    // BigIntegerTerm carrying the exact value.
    [Theory]
    [InlineData("999999999999999999999999999999", "999999999999999999999999999999")]
    [InlineData("+999999999999999999999999999999", "999999999999999999999999999999")]
    [InlineData("-999999999999999999999999999999", "-999999999999999999999999999999")]
    [InlineData("0xffffffffffffffffffffffffffffffff", "340282366920938463463374607431768211455")]
    [InlineData("-0xffffffffffffffffffffffffffffffff", "-340282366920938463463374607431768211455")]
    public void ReadsIntegerLiteralsBeyondTheLongRange(string source, string expected)
    {
        ArgumentNullException.ThrowIfNull(source);

        ParseResult result = TermReader.ReadTerm(source, "limit.pl");

        Assert.Empty(result.Diagnostics);
        BigIntegerTerm big = Assert.IsType<BigIntegerTerm>(Assert.Single(result.Clauses));
        Assert.Equal(System.Numerics.BigInteger.Parse(expected, CultureInfo.InvariantCulture), big.Value);
    }

    [Theory]
    [InlineData("1.0e9999", 0, 8)]
    [InlineData("+1.0e9999", 0, 9)]
    [InlineData("-1.0e9999", 0, 9)]
    [InlineData("f(1.0e9999)", 2, 8)]
    public void ReportsFloatOverflowWithItsSourceSpan(string source, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        ParseResult result = TermReader.ReadTerm(source, "limit.pl");

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticIds.FloatOverflow, diagnostic.Id);
        Assert.Equal(new SourceSpan(start, length, 1, start + 1), diagnostic.Span);
        Assert.Equal("limit.pl", diagnostic.FileName);
    }

    [Fact]
    public void CharacterConversionChangesOnlyUnquotedTokenText()
    {
        var conversions = new CharacterConversionTable();
        var flags = new PrologFlags();
        conversions.Set('z', 'x');
        flags.SetCharConversion(enabled: true);

        ParseResult result = TermReader.ReadTerm("fizz('fizz', \"fizz\")", characterConversions: conversions, flags: flags);

        Assert.True(result.Success);
        Assert.Equal("fixx(fizz,\"fizz\")", Canonical(Assert.Single(result.Clauses)));
    }

    [Fact]
    public void CharacterConversionDirectivesAffectTheNextClauseFirstToken()
    {
        var conversions = new CharacterConversionTable();
        var flags = new PrologFlags();

        ParseResult result = TermReader.ReadProgram(
            """
            :- char_conversion(z, x).
            :- set_prolog_flag(char_conversion, on).
            fizz.
            """,
            characterConversions: conversions,
            flags: flags
        );

        Assert.True(result.Success);
        Assert.Equal("fixx", Canonical(result.Clauses[^1]));
    }

    [Fact]
    public void ARejectedOperatorDirectiveDoesNotPartiallyChangeTheReaderTable()
    {
        var operators = new OperatorTable();

        ParseResult result = TermReader.ReadProgram(":- op(100, xfx, [temporary_operator, ',']).", operators: operators);

        Assert.True(result.Success);
        Assert.False(operators.IsOperator("temporary_operator"));
    }
}
