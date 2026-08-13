using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The interned string term of ADR 0047: a seventh cell tag over the atom text table, the
/// double_quotes string value gated out of strict mode, SWI's probed standard-order slot between
/// numbers and atoms, and the string library. Expected values are pinned against SWI-Prolog 10.
/// </summary>
public sealed class StringTypeTests
{
    private static string RunString(string goal) =>
        PrologTestHost.Run(
            $"""
            :- set_prolog_flag(double_quotes, string).
            :- initialization(({goal})).
            """
        );

    [Fact]
    public void AStringIsAtomicButNeitherAtomNorCallable() =>
        Assert.Equal(
            "s na t nc g",
            RunString(
                "X = \"abc\", ( string(X) -> write(s) ; write(ns) ), write(' '),"
                    + " ( atom(X) -> write(a) ; write(na) ), write(' '),"
                    + " ( atomic(X) -> write(t) ; write(nt) ), write(' '),"
                    + " ( callable(X) -> write(c) ; write(nc) ), write(' '),"
                    + " ( ground(X) -> write(g) ; write(ng) )"
            )
        );

    [Fact]
    public void AStringUnifiesOnlyWithItself() =>
        Assert.Equal(
            "nu eq neq",
            RunString(
                "( \"abc\" = abc -> write(u) ; write(nu) ), write(' '),"
                    + " ( \"abc\" == \"abc\" -> write(eq) ; write(neq) ), write(' '),"
                    + " ( \"abc\" == \"abd\" -> write(eq2) ; write(neq) )"
            )
        );

    // SWI-Prolog 10 sorts strings after numbers and before atoms; its manual still documents the
    // older order, and the probe wins (ADR 0047).
    [Fact]
    public void StandardOrderPlacesStringsBetweenNumbersAndAtoms() =>
        Assert.Equal("[2.5,1,\"a\",\"b\",zz,f(x)]", RunString("msort([f(x), zz, \"b\", 1, 2.5, \"a\"], L), writeq(L)"));

    [Fact]
    public void WriteqQuotesAndEscapesWhileWriteIsBare() =>
        Assert.Equal("\"a\\nb\" a b", RunString("X = \"a\\nb\", writeq(X), write(' '), Y = \"a b\", write(Y)"));

    [Fact]
    public void FunctorAndUnivTreatAStringAsAtomic() =>
        Assert.Equal("\"ab\"-0 [\"ab\"]", RunString("functor(\"ab\", F, A), writeq(F-A), write(' '), \"ab\" =.. L, writeq(L)"));

    [Fact]
    public void AssertedStringsSurviveTheClauseRoundTrip() =>
        Assert.Equal("\"kept\"", RunString("assertz(str_fact(\"kept\")), str_fact(Y), ( string(Y) -> writeq(Y) ; write(lost) )"));

    [Fact]
    public void DetachedCopiesKeepTheStringTag() =>
        Assert.Equal(
            "\"cp\" \"nb\" [\"f\"]",
            RunString(
                "copy_term(\"cp\", C), writeq(C), write(' '),"
                    + " nb_setval(k, \"nb\"), nb_getval(k, V), writeq(V), write(' '),"
                    + " findall(X, X = \"f\", L), writeq(L)"
            )
        );

    [Fact]
    public void ArithmeticRejectsAString() =>
        Assert.Equal("type_error(evaluable,\"a\")", RunString("catch(_ is \"a\" + 1, error(E, _), true), writeq(E)"));

    // A string literal in a grammar body matches its code list even under the string flag,
    // SWI-Prolog 10's probed behavior.
    [Fact]
    public void DcgStringLiteralsMatchCodeLists() =>
        Assert.Equal(
            "codes_match",
            PrologTestHost.Run(
                """
                :- set_prolog_flag(double_quotes, string).
                greet --> "hi", [x].
                :- initialization(( ( phrase(greet, [0'h, 0'i, x]) -> write(codes_match) ; write(codes_no) ) )).
                """
            )
        );

    [Fact]
    public void TheFlagValueIsScopedToItsLoadUnitAndAbsentFromStrictMode()
    {
        Assert.Equal(
            "string codes",
            PrologTestHost.Run(
                """
                :- set_prolog_flag(double_quotes, string).
                :- current_prolog_flag(double_quotes, V), write(V).
                """ + "\n:- initialization(( write(' '), current_prolog_flag(double_quotes, W), write(W) )).\n"
            )
        );

        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal(
                "catch(set_prolog_flag(double_quotes, string), error(E, _), E == domain_error(flag_value, double_quotes + string))",
                out _
            )
        );
    }

    [Fact]
    public void TheOverrideSurfaceAcceptsStringOutsideStrictMode()
    {
        var engine = new PrologEngine(
            PrologLanguageMode.Extended,
            new PrologFlagOverrides { DoubleQuotes = DoubleQuotesMode.String }
        )
        {
            Output = new StringWriter(),
        };

        Assert.Equal(DoubleQuotesMode.String, engine.Program.InitialDoubleQuotes);
        Assert.Equal(RunResult.Success, engine.RunGoal("\"ab\" == \"ab\", string(\"ab\")", out _));

        Assert.Throws<ArgumentException>(() =>
            new PrologEngine(PrologLanguageMode.StrictIso, new PrologFlagOverrides { DoubleQuotes = DoubleQuotesMode.String })
        );
    }

    [Theory]
    [InlineData("string_length(\"abc\", N), writeq(N)", "3")]
    [InlineData("string_length(foo, N), writeq(N)", "3")]
    [InlineData("string_concat(\"ab\", \"cd\", R), writeq(R)", "\"abcd\"")]
    [InlineData("string_concat(ab, 12, R), writeq(R)", "\"ab12\"")]
    [InlineData("findall(A-B, string_concat(A, B, \"ab\"), L), writeq(L)", "[\"\"-\"ab\",\"a\"-\"b\",\"ab\"-\"\"]")]
    [InlineData("string_concat(ab, B, \"abcd\"), writeq(B)", "\"cd\"")]
    [InlineData("atom_string(A, \"foo\"), writeq(A)", "foo")]
    [InlineData("atom_string(bar, S), writeq(S)", "\"bar\"")]
    [InlineData("string_to_atom(\"baz\", B), writeq(B)", "baz")]
    [InlineData("string_to_atom(S, qux), writeq(S)", "\"qux\"")]
    [InlineData("string_chars(S, [a, b]), writeq(S)", "\"ab\"")]
    [InlineData("string_chars(\"xy\", C), writeq(C)", "[x,y]")]
    [InlineData("string_codes(\"xy\", K), writeq(K)", "[120,121]")]
    [InlineData("number_string(N, \"42\"), writeq(N)", "42")]
    [InlineData("number_string(N, \" 3.5 \"), writeq(N)", "3.5")]
    [InlineData("( number_string(_, \"abc\") -> write(y) ; write(fails) )", "fails")]
    [InlineData("number_string(3, S), writeq(S)", "\"3\"")]
    [InlineData("term_string(T, \"f(X, 1)\"), functor(T, N, A), writeq(N/A)", "f/2")]
    [InlineData("term_string(g(a), S), writeq(S)", "\"g(a)\"")]
    [InlineData("split_string(\"a,b,,c\", \",\", \"\", L), writeq(L)", "[\"a\",\"b\",\"\",\"c\"]")]
    [InlineData("split_string(\"/a//b/\", \"/\", \"/\", L), writeq(L)", "[\"a\",\"b\"]")]
    [InlineData("split_string(\"  hi  \", \"\", \" \", L), writeq(L)", "[\"hi\"]")]
    [InlineData("split_string(\"a/\", \"/\", \"/\", L), writeq(L)", "[\"a\"]")]
    [InlineData("split_string(\"//\", \"/\", \"/\", L), writeq(L)", "[\"\"]")]
    [InlineData("split_string(\"/a/\", \"/\", \"\", L), writeq(L)", "[\"\",\"a\",\"\"]")]
    [InlineData("sub_string(\"abcde\", 1, 3, A, S), writeq(S-A)", "\"bcd\"-1")]
    [InlineData("findall(B-A, sub_string(\"aba\", B, 2, A, \"ab\"), L), writeq(L)", "[0-1]")]
    [InlineData("aggregate_all(count, sub_string(\"abc\", _, _, _, _), N), writeq(N)", "10")]
    [InlineData("string_code(2, \"abc\", C), writeq(C)", "98")]
    [InlineData("findall(I-C, string_code(I, \"abc\", C), L), writeq(L)", "[1-97,2-98,3-99]")]
    [InlineData("string_lower(\"AbC\", L), string_upper(\"AbC\", U), writeq(L-U)", "\"abc\"-\"ABC\"")]
    [InlineData("with_output_to(string(S), write(hello)), writeq(S)", "\"hello\"")]
    [InlineData("format(string(S), \"~w\", [42]), writeq(S)", "\"42\"")]
    [InlineData("format(\"~s\", [\"str\"])", "str")]
    [InlineData("must_be(string, \"ok\"), write(ok)", "ok")]
    [InlineData("catch(must_be(string, foo), error(E, _), true), writeq(E)", "type_error(string,foo)")]
    [InlineData("( is_of_type(text, \"txt\") -> write(y) ; write(n) )", "y")]
    public void StringLibraryMatchesSwi(string goal, string expected) => Assert.Equal(expected, RunString(goal));

    [Theory]
    [InlineData("catch(string_length(_, _), error(E, _), true), writeq(E)", "instantiation_error")]
    [InlineData("catch(string_length(f(x), _), error(E, _), true), writeq(E)", "type_error(string,f(x))")]
    [InlineData("catch(string_concat(_, _, _), error(E, _), true), writeq(E)", "instantiation_error")]
    public void StringLibraryValidatesItsArguments(string goal, string expected) => Assert.Equal(expected, RunString(goal));
}
