using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Characters are Unicode code points in the default mode, so a character outside the Basic
/// Multilingual Plane is one character with one code, while strict ISO mode keeps its UTF-16
/// code-unit characters unchanged. Agreement with SWI-Prolog is checked by the differential corpus.
/// </summary>
public sealed class CodePointCharacterTests
{
    [Theory]
    [InlineData("atom_length('a😀b', N), write(N)", "3")]
    [InlineData("atom_codes('a😀b', L), write(L)", "[97,128512,98]")]
    [InlineData("atom_chars('a😀b', L), length(L, N), write(N)", "3")]
    [InlineData("atom_codes(A, [128512, 97]), atom_length(A, N), write(N)", "2")]
    [InlineData("char_code('😀', C), write(C)", "128512")]
    [InlineData("char_code(C, 128512), atom_length(C, N), write(N)", "1")]
    [InlineData("sub_atom('a😀b', 1, 1, A, S), atom_length(S, N), write(A-N)", "1-1")]
    [InlineData("sub_atom('a😀b', B, 1, 0, b), write(B)", "2")]
    [InlineData("findall(B-L, sub_atom('a😀b', B, L, _, _), All), length(All, N), write(N)", "10")]
    [InlineData("findall(X, atom_concat(X, _, 'a😀b'), All), length(All, N), write(N)", "4")]
    [InlineData("char_code(D, 0x10428), upcase_atom(D, U), char_code(U, C), write(C)", "66560")]
    [InlineData("char_code(E, 128512), atom_concat('0\\'', E, T), atom_number(T, N), write(N)", "128512")]
    public void AtomPredicatesCountCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("string_length(\"a😀b\", N), write(N)", "3")]
    [InlineData("sub_string('a😀b', 1, 1, A, S), string_length(S, N), write(A-N)", "1-1")]
    [InlineData("string_code(2, \"a😀b\", C), write(C)", "128512")]
    [InlineData("split_string(\"a😀b\", \"😀\", \"\", P), length(P, N), write(N)", "2")]
    public void StringPredicatesCountCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("X = \"😀é\", length(X, N), write(N)", "2")]
    [InlineData("X = \"😀é\", X = [E|_], char_code(E, C), write(C)", "128512")]
    public void DoubleQuotedTextSplitsIntoCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void DoubleQuotedCodesAreCodePoints() =>
        Assert.Equal(
            "[128512,233]",
            PrologTestHost.Run(":- set_prolog_flag(double_quotes, codes).\n:- initialization((X = \"😀é\", write(X))).\n")
        );

    [Theory]
    [InlineData("char_code(_, 0xD800)", "representation_error(character_code)")]
    [InlineData("char_code(_, 0x110000)", "representation_error(character_code)")]
    [InlineData("atom_codes(_, [0xDC00])", "representation_error(character_code)")]
    [InlineData("code_type(0x110000, _)", "domain_error(character,1114112)")]
    [InlineData("string_length([0xD800], _)", "type_error(character_code,55296)")]
    public void SurrogatesAndCodesBeyondUnicodeAreNotCharacters(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), true), writeq(E)"));

    [Theory]
    [InlineData("char_code(D, 0x10400), char_type(D, upper(L)), char_code(L, C), write(C)", "66600")]
    [InlineData("char_code(E, 128512), ( char_type(E, graph) -> write(yes) ; write(no) )", "yes")]
    [InlineData("( must_be(code, 128512) -> write(yes) ; write(no) )", "yes")]
    public void ClassificationCoversSupplementaryCharacters(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("X = 0'😀, write(X)", "128512")]
    [InlineData("atom_codes('\\x1F600\\\\U0001F601', L), write(L)", "[128512,128513]")]
    [InlineData("X = 𝑎bc, atom(X), atom_length(X, N), write(N)", "3")]
    [InlineData("writeq(['𝑎bc', 'a😀b', 'x y'])", "[𝑎bc,'a😀b','x y']")]
    [InlineData("term_to_atom(T, '𝑎bc(𐐀x)'), T = 𝑎bc(V), ( var(V) -> write(var) ; write(bound) )", "var")]
    public void TheReaderAndWriterUseCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("format(\"~w~t~6|x\", ['😀'])", "😀     x")]
    [InlineData("format(\"~`😀t~3|\", []), nl", "😀😀😀\n")]
    [InlineData("format(\"~c~c\", [128512, 97])", "😀a")]
    [InlineData("catch(format(\"~c\", [0xD800]), error(E, _), true), writeq(E)", "representation_error(code_point)")]
    [InlineData("catch(format(\"~c\", [-1]), error(E, _), true), writeq(E)", "format_argument_type(c,-1)")]
    public void FormatCountsColumnsInCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void StreamsReadAndPeekWholeCharacters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotprolog-codepoints-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "a😀😀b.");
            Assert.Equal(
                "[a,128512,128512,'😀',b]",
                PrologTestHost.RunGoal(
                    $"open('{path.Replace("\\", "\\\\", StringComparison.Ordinal)}', read, S), get_char(S, A), peek_code(S, P), get_code(S, C), get_char(S, D), read(S, T), close(S), writeq([A, P, C, D, T])"
                )
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("put_char('😀'), put_code(128513)", "😀😁")]
    [InlineData(
        "char_conversion('😀', x), set_prolog_flag(char_conversion, on), read_term_from_atom('f(😀)', T, []), writeq(T)",
        "f(x)"
    )]
    public void OutputAndConversionUseCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData(
        "msort(['\\x1F600\\', '\\xFFFD\\', a, '\\xE000\\'], L), maplist(char_code, L, C), write(C)",
        "[97,57344,65533,128512]"
    )]
    [InlineData("atom_string('😀', S), atom_string('\uFFFD', R), compare(O, S, R), write(O)", ">")]
    [InlineData("compare(O, 'a😀'(x), 'a\uE000'(x)), write(O)", ">")]
    [InlineData("compare(O, '😀', '😁'), write(O)", "<")]
    [InlineData("sort(0, @>=, ['\uFFFD', '😀', '\uFFFD'], L), maplist(char_code, L, C), write(C)", "[128512,65533,65533]")]
    public void StandardOrderComparesCodePoints(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("writeq('𝑎bc')", "'𝑎bc'")]
    [InlineData("atom_length('a😀b', N), write(N)", "4")]
    [InlineData("atom_codes('😀', L), write(L)", "[55357,56832]")]
    [InlineData("catch(char_code(_, 128512), error(E, _), true), writeq(E)", "representation_error(character_code)")]
    [InlineData("findall(B-L, sub_atom('a😀b', B, L, _, _), All), All = [_|_], write(done)", "done")]
    [InlineData("catch(put_code(65536), error(E, _), true), writeq(E)", "representation_error(character_code)")]
    [InlineData("catch(get_code(65536), error(E, _), true), writeq(E)", "representation_error(in_character_code)")]
    [InlineData("sort(['\uE000', '😀'], [F|_]), atom_length(F, N), write(N)", "2")]
    public void StrictModeKeepsCodeUnitCharacters(string goal, string expected)
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output };

        Assert.True(engine.ConsultText($":- initialization(({goal})).\n").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal(expected, output.ToString());
    }
}
