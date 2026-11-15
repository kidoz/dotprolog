using System.Globalization;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Syntax.Tests;

public sealed class TermReaderTests
{
    [Theory]
    [InlineData("parentheses")]
    [InlineData("braces")]
    [InlineData("arguments")]
    [InlineData("lists")]
    [InlineData("prefix")]
    [InlineData("right")]
    [InlineData("left")]
    [InlineData("flat-list")]
    [InlineData("string")]
    public void ExcessiveDepthProducesOnePositionedDiagnosticAndRecovers(string shape)
    {
        const int depth = 20_000;
        var source = shape switch
        {
            "parentheses" => new string('(', depth) + "a" + new string(')', depth),
            "braces" => new string('{', depth) + "a" + new string('}', depth),
            "arguments" => string.Concat(Enumerable.Repeat("f(", depth)) + "a" + new string(')', depth),
            "lists" => new string('[', depth) + "a" + new string(']', depth),
            "prefix" => string.Concat(Enumerable.Repeat("+ ", depth)) + "a",
            "right" => string.Concat(Enumerable.Repeat("a ^ ", depth)) + "a",
            "left" => string.Concat(Enumerable.Repeat("a + ", depth)) + "a",
            "flat-list" => "[" + string.Join(",", Enumerable.Repeat("a", depth)) + "]",
            _ => "\"" + new string('a', depth) + "\"",
        };

        var parsed = TermReader.ReadProgram("\n" + source + ". recovered.", "deep.pl");
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.False(parsed.Success);
        Assert.Equal(DiagnosticIds.NestingLimitExceeded, diagnostic.Id);
        Assert.Equal("deep.pl", diagnostic.FileName);
        Assert.Equal(2, diagnostic.Span.Line);
        Assert.True(diagnostic.Span.Column > 0);
        Assert.Equal("recovered", Assert.IsType<AtomTerm>(Assert.Single(parsed.Clauses)).Name);

        var single = TermReader.ReadTerm(source, "deep.pl");
        Assert.Empty(single.Clauses);
        Assert.Equal(DiagnosticIds.NestingLimitExceeded, Assert.Single(single.Diagnostics).Id);
    }

    [Theory]
    [InlineData(DoubleQuotesMode.Atom)]
    [InlineData(DoubleQuotesMode.String)]
    public void LongAtomicTextDoesNotConsumeTermDepth(DoubleQuotesMode mode)
    {
        var flags = new PrologFlags();
        flags.SetDoubleQuotes(mode);
        var parsed = TermReader.ReadTerm("\"" + new string('a', 20_000) + "\"", flags: flags);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics));
        Assert.Single(parsed.Clauses);
    }

    [Fact]
    public void ListSpinesObserveTheResultingTermDepthBoundary()
    {
        var accepted = "[" + string.Join(",", Enumerable.Repeat("a", TermReader.MaxTermDepth - 1)) + "]";
        Assert.True(TermReader.ReadTerm(accepted).Success);
        var rejected = "[" + string.Join(",", Enumerable.Repeat("a", TermReader.MaxTermDepth)) + "]";
        Assert.Equal(DiagnosticIds.NestingLimitExceeded, Assert.Single(TermReader.ReadTerm(rejected).Diagnostics).Id);
    }

    [Fact]
    public void SmallHostThreadStackStillProducesADiagnostic()
    {
        var source = string.Concat(Enumerable.Repeat("f(", 20_000)) + "a" + new string(')', 20_000) + ".";
        ParseResult? parsed = null;
        var worker = new Thread(() => parsed = TermReader.ReadProgram(source, "small-stack.pl"), 128 * 1024);
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.NotNull(parsed);
        Assert.Equal(DiagnosticIds.NestingLimitExceeded, Assert.Single(parsed.Diagnostics).Id);
    }

    [Fact]
    public void DepthBudgetResetsBetweenClausesAndSiblingArguments()
    {
        var term = new string('(', 128) + "a" + new string(')', 128);
        Assert.True(TermReader.ReadProgram($"p({term},{term}). p({term},{term}).").Success);
        Assert.True(TermReader.ReadTerm(new string('(', 128) + "a" + new string(')', 128)).Success);
    }

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
        var result = TermReader.ReadProgram(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return Canonical(Assert.Single(result.Clauses));
    }

    [Fact]
    public void ReadsFactWithQuotedAtom()
    {
        Assert.Equal("greeting(Hello! World!)", ReadSingle("greeting('Hello! World!')."));
    }

    [Theory]
    [InlineData("1 \"+\" 2 * 3", "+(1,*(2,3))")]
    [InlineData("\"-\"7", "-(7)")]
    [InlineData("\"pair\"(a,b)", "pair(a,b)")]
    public void AtomValuedDoubleQuotesUseAtomGrammar(string source, string expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        var flags = new PrologFlags();
        flags.SetDoubleQuotes(DoubleQuotesMode.Atom);
        var parsed = TermReader.ReadTerm(source, flags: flags);

        Assert.Empty(parsed.Diagnostics);
        var term = Assert.Single(parsed.Clauses);
        Assert.Equal(expected, Canonical(term));
        Assert.Equal(0, term.Span.Start);
        Assert.Equal(source.Length, term.Span.Length);
    }

    [Fact]
    public void AtomValuedDoubleQuotedOperatorsStillEnforceAssociativity()
    {
        var flags = new PrologFlags();
        flags.SetDoubleQuotes(DoubleQuotesMode.Atom);
        var parsed = TermReader.ReadTerm("a \"=\" b \"=\" c", "quoted.pl", flags: flags);

        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(DiagnosticIds.UnexpectedToken, diagnostic.Id);
        Assert.Equal("quoted.pl", diagnostic.FileName);
        Assert.Equal(8, diagnostic.Span.Start);
        Assert.Equal(3, diagnostic.Span.Length);
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
        var result = TermReader.ReadProgram("X is 2 ** 3 ** 2.");

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
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(255)]
    [InlineData(256)]
    public void CompoundArgumentsKeepTheirOrderAndSpansAcrossNestedTerms(int arity)
    {
        var arguments = Enumerable.Range(0, arity).Select(i => $"pair({i},X)").ToArray();
        var termText = $"outer({string.Join(',', arguments)})";
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var result = TermReader.ReadProgram($"{termText}.\nnext(ok).\n", flags: new BytecodeProgram(mode).Flags);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.Clauses.Count);
            var term = Assert.IsType<CompoundTerm>(result.Clauses[0]);
            Assert.Equal("outer", term.Name);
            Assert.Equal(arity, term.Arity);
            Assert.Equal(new SourceSpan(0, termText.Length, 1, 1), term.Span);
            for (var i = 0; i < arity; i++)
            {
                Assert.Equal(arguments[i], Canonical(term.Arguments[i]));
                var start = termText.IndexOf(arguments[i], StringComparison.Ordinal);
                Assert.Equal(new SourceSpan(start, arguments[i].Length, 1, start + 1), term.Arguments[i].Span);
            }

            Assert.Equal("next(ok)", Canonical(result.Clauses[1]));
            Assert.Equal(new SourceSpan(termText.Length + 2, 8, 2, 1), result.Clauses[1].Span);
        }
    }

    [Theory]
    [InlineData("broken().", 7)]
    [InlineData("broken(a,).", 9)]
    [InlineData("broken(a,b,).", 11)]
    [InlineData("broken(a,b,c,).", 13)]
    [InlineData("broken(a b).", 9)]
    [InlineData("broken(a,b c).", 11)]
    [InlineData("broken(a,b,c d).", 13)]
    public void MalformedArgumentsKeepTheDiagnosticAndFollowingClause(string broken, int errorStart)
    {
        ArgumentNullException.ThrowIfNull(broken);
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var result = TermReader.ReadProgram(
                $"{broken}\ngood(z).\n",
                "arguments.pl",
                flags: new BytecodeProgram(mode).Flags
            );

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticIds.UnexpectedToken, diagnostic.Id);
            Assert.Equal(new SourceSpan(errorStart, 1, 1, errorStart + 1), diagnostic.Span);
            Assert.Equal("arguments.pl", diagnostic.FileName);
            var following = Assert.Single(result.Clauses);
            Assert.Equal("good(z)", Canonical(following));
            Assert.Equal(new SourceSpan(broken.Length + 1, 7, 2, 1), following.Span);
        }
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
        var result = TermReader.ReadProgram("broken(a b).\ngood(c).");

        Assert.False(result.Success);
        Assert.Equal(DiagnosticIds.UnexpectedToken, result.Diagnostics[0].Id);
        Assert.Equal("good(c)", Canonical(Assert.Single(result.Clauses)));
    }

    [Fact]
    public void ReportsAMissingClauseTerminator()
    {
        var result = TermReader.ReadProgram("a\nb.");

        Assert.Equal(DiagnosticIds.MissingEndToken, Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void DiagnosticCarriesLineAndColumn()
    {
        var result = TermReader.ReadProgram("p(a).\nq(b c).", "test.pl");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Span.Line);
        Assert.Equal("test.pl", diagnostic.FileName);
        Assert.StartsWith("test.pl(2,", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTermAcceptsAGoalWithoutATerminator()
    {
        var result = TermReader.ReadTerm("write(hi), nl");

        Assert.True(result.Success);
        Assert.Equal(",(write(hi),nl)", Canonical(Assert.Single(result.Clauses)));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("1e2")]
    public void ReadTermRejectsTrailingTokens(string source)
    {
        var result = TermReader.ReadTerm(source);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticIds.UnexpectedToken, Assert.Single(result.Diagnostics).Id);
    }

    // Integers are unbounded: a literal past the long range parses to a
    // BigIntegerTerm carrying the exact value.
    [Theory]
    [InlineData("999999999999999999999999999999", "999999999999999999999999999999")]
    [InlineData("-999999999999999999999999999999", "-999999999999999999999999999999")]
    [InlineData("0xffffffffffffffffffffffffffffffff", "340282366920938463463374607431768211455")]
    [InlineData("-0xffffffffffffffffffffffffffffffff", "-340282366920938463463374607431768211455")]
    public void ReadsIntegerLiteralsBeyondTheLongRange(string source, string expected)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = TermReader.ReadTerm(source, "limit.pl");

        Assert.Empty(result.Diagnostics);
        var big = Assert.IsType<BigIntegerTerm>(Assert.Single(result.Clauses));
        Assert.Equal(System.Numerics.BigInteger.Parse(expected, CultureInfo.InvariantCulture), big.Value);
    }

    // Cor.2: a plus is never part of a number, so +1 is +(1) and + is a prefix operator there.
    [Theory]
    [InlineData("+1", "+", 1.0)]
    [InlineData("+ 1", "+", 1.0)]
    [InlineData("+1.5", "+", 1.5)]
    public void ReadsAPlusBeforeANumberAsAPrefixOperator(string source, string name, double operand)
    {
        var result = TermReader.ReadTerm(source);

        Assert.Empty(result.Diagnostics);
        var term = Assert.IsType<CompoundTerm>(Assert.Single(result.Clauses));
        Assert.Equal(name, term.Name);
        var value = Assert.Single(term.Arguments) switch
        {
            IntegerTerm integer => integer.Value,
            FloatTerm number => number.Value,
            SyntaxTerm other => throw new InvalidOperationException($"Unexpected operand {other}."),
        };
        Assert.Equal(operand, value);
    }

    [Theory]
    [InlineData("- 1", -1)]
    [InlineData("'-'1", -1)]
    [InlineData("- /**/ 1", -1)]
    public void ReadsAMinusBeforeANumberAsANegativeNumber(string source, long expected)
    {
        var result = TermReader.ReadTerm(source);

        Assert.Empty(result.Diagnostics);
        var term = Assert.IsType<IntegerTerm>(Assert.Single(result.Clauses));
        Assert.Equal(expected, term.Value);
    }

    [Fact]
    public void ReadsTheEmptyListNameWithASpaceAsAFunctor()
    {
        var result = TermReader.ReadTerm("[ ](1)");
        Assert.Empty(result.Diagnostics);
        var term = Assert.IsType<CompoundTerm>(Assert.Single(result.Clauses));
        Assert.Equal("[]", term.Name);
        Assert.Equal(1, term.Arity);

        Assert.NotEmpty(TermReader.ReadTerm("[ ] (1)").Diagnostics);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void ReadsTheEmptyBraceNameWithASpaceAsAFunctor(PrologLanguageMode mode)
    {
        var result = TermReader.ReadTerm("{ }(a)", flags: new BytecodeProgram(mode).Flags);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal("{}(a)", Canonical(Assert.Single(result.Clauses)));
        Assert.NotEmpty(TermReader.ReadTerm("{ } (a)", flags: new BytecodeProgram(mode).Flags).Diagnostics);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void RedefinedBarKeepsItsOwnFunctor(PrologLanguageMode mode)
    {
        var result = TermReader.ReadProgram(
            ":- op(1100, xfy, '|').\nX = (a|b).\n",
            flags: new BytecodeProgram(mode).Flags
        );

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal("=(X,|(a,b))", Canonical(result.Clauses[^1]));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern, "- '->'(a,b)", "-(->(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "- '->'(a,b)", "-(->(a,b))")]
    [InlineData(PrologLanguageMode.Modern, "- ;(a,b)", "-(;(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "- ;(a,b)", "-(;(a,b))")]
    [InlineData(PrologLanguageMode.Modern, "- ','(a,b)", "-(,(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "- ','(a,b)", "-(,(a,b))")]
    [InlineData(PrologLanguageMode.Modern, "- '|'(a,b)", "-(|(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "- '|'(a,b)", "-(|(a,b))")]
    [InlineData(PrologLanguageMode.Modern, "- mod(a,b)", "-(mod(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "- mod(a,b)", "-(mod(a,b))")]
    [InlineData(PrologLanguageMode.Modern, "\\+ =(a,b)", "\\+(=(a,b))")]
    [InlineData(PrologLanguageMode.StrictIso, "\\+ =(a,b)", "\\+(=(a,b))")]
    public void PrefixOperatorsAcceptOperatorFunctorsAsOperands(PrologLanguageMode mode, string source, string expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = TermReader.ReadTerm(source, "operators.pl", flags: new BytecodeProgram(mode).Flags);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var term = Assert.Single(result.Clauses);
        Assert.Equal(expected, Canonical(term));
        Assert.Equal(new SourceSpan(0, source.Length, 1, 1), term.Span);
    }

    [Fact]
    public void PublicReaderDefaultsFollowTheSuppliedReadingFlags()
    {
        var strictAtom = TermReader.ReadTerm("X = xor", flags: new BytecodeProgram(PrologLanguageMode.StrictIso).Flags);
        var strictInfix = TermReader.ReadTerm(
            "module - 1",
            flags: new BytecodeProgram(PrologLanguageMode.StrictIso).Flags
        );
        var modernAtom = TermReader.ReadTerm("X = xor", flags: new BytecodeProgram(PrologLanguageMode.Modern).Flags);
        var modernPrefix = TermReader.ReadTerm("module - 1", flags: new BytecodeProgram(PrologLanguageMode.Modern).Flags);

        Assert.Equal("=(X,xor)", Canonical(Assert.Single(strictAtom.Clauses)));
        Assert.Equal("-(module,1)", Canonical(Assert.Single(strictInfix.Clauses)));
        Assert.Equal("=(X,xor)", Canonical(Assert.Single(modernAtom.Clauses)));
        Assert.Equal("module(-1)", Canonical(Assert.Single(modernPrefix.Clauses)));
    }

    [Fact]
    public void StrictIsoRejectsAnAdjacentNumberAfterADoubleQuotedMinusAtom()
    {
        var strict = new BytecodeProgram(PrologLanguageMode.StrictIso).Flags;
        strict.SetDoubleQuotes(DoubleQuotesMode.Atom);
        var rejected = TermReader.ReadTerm("\"-\"1", "quoted.pl", flags: strict);

        Assert.False(rejected.Success);
        var diagnostic = Assert.Single(rejected.Diagnostics);
        Assert.Equal(DiagnosticIds.UnexpectedToken, diagnostic.Id);
        Assert.Equal(new SourceSpan(3, 1, 1, 4), diagnostic.Span);

        var modern = new BytecodeProgram(PrologLanguageMode.Modern).Flags;
        modern.SetDoubleQuotes(DoubleQuotesMode.Atom);
        var accepted = TermReader.ReadTerm("\"-\"1", flags: modern);
        Assert.Equal("-(1)", Canonical(Assert.Single(accepted.Clauses)));
    }

    [Fact]
    public void StrictIsoRequiresLayoutOrACommentAfterTheEndChar()
    {
        var strict = TermReader.ReadProgram("a.", flags: new BytecodeProgram(PrologLanguageMode.StrictIso).Flags);
        var modern = TermReader.ReadProgram("a.", flags: new BytecodeProgram(PrologLanguageMode.Modern).Flags);

        Assert.Equal(DiagnosticIds.MissingEndToken, Assert.Single(strict.Diagnostics).Id);
        Assert.True(modern.Success, string.Join("; ", modern.Diagnostics));
        Assert.Equal("a", Canonical(Assert.Single(modern.Clauses)));
        Assert.True(TermReader.ReadProgram("a.%", flags: new BytecodeProgram(PrologLanguageMode.StrictIso).Flags).Success);
    }

    [Theory]
    [InlineData("1.0e9999", 0, 8)]
    [InlineData("+1.0e9999", 1, 8)]
    [InlineData("-1.0e9999", 0, 9)]
    [InlineData("f(1.0e9999)", 2, 8)]
    public void ReportsFloatOverflowWithItsSourceSpan(string source, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = TermReader.ReadTerm(source, "limit.pl");

        var diagnostic = Assert.Single(result.Diagnostics);
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

        var result = TermReader.ReadTerm("fizz('fizz', \"fizz\")", characterConversions: conversions, flags: flags);

        Assert.True(result.Success);
        Assert.Equal("fixx(fizz,\"fizz\")", Canonical(Assert.Single(result.Clauses)));
    }

    [Fact]
    public void CharacterConversionDirectivesAffectTheNextClauseFirstToken()
    {
        var conversions = new CharacterConversionTable();
        var flags = new PrologFlags();

        var result = TermReader.ReadProgram(
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

        var result = TermReader.ReadProgram(":- op(100, xfx, [temporary_operator, ',']).", operators: operators);

        Assert.True(result.Success);
        Assert.False(operators.IsOperator("temporary_operator"));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void PunctuationConversionChangesAtClauseBoundaries(PrologLanguageMode mode)
    {
        var conversions = new CharacterConversionTable();
        var flags = new BytecodeProgram(mode).Flags;
        var result = TermReader.ReadProgram(
            """
            :- char_conversion('#', ',').
            :- set_prolog_flag(char_conversion, on).
            pair(a#b).
            :- char_conversion('#', '|').
            list([a#Tail]).
            :- set_prolog_flag(char_conversion, off).
            atom(#).
            """ + "\n",
            characterConversions: conversions,
            flags: flags
        );

        Assert.True(result.Success);
        Assert.Equal("pair(a,b)", Canonical(result.Clauses[2]));
        Assert.Equal("list(.(a,Tail))", Canonical(result.Clauses[4]));
        Assert.Equal("atom(#)", Canonical(result.Clauses[6]));
    }
}
