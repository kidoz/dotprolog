using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// <c>number_chars/2</c> against the ISO conformity table at
/// https://www.complang.tuwien.ac.at/ulrich/iso-prolog/number_chars_cont, whose case numbers the
/// comments cite. A list that holds a whole number's text is read with the term reader's number
/// syntax and the result unified with the first argument; where the table allows several answers,
/// the one DotProlog gives is pinned.
/// </summary>
public sealed class NumberCharsConformityTests
{
    [Theory]
    [InlineData("number_chars(1.2, \"1.20\")")] // 55
    [InlineData("number_chars(1.0e9, \"1.0E9\")")] // 2
    [InlineData("number_chars(1.0e9, \"1.0e9\")")] // 56
    [InlineData("number_chars(0.0, \"-0.0\")")] // 64
    [InlineData("number_chars(1, \"01\")")] // 3
    [InlineData("number_chars(10, \"010\")")] // 65
    [InlineData("number_chars(N, \"0b11\"), N == 3")] // 68
    [InlineData("number_chars(N, \" 1\"), N == 1")] // 18
    [InlineData("number_chars(N, \"\\n1\"), N == 1")] // 19
    [InlineData("number_chars(N, \" 0'a\"), N == 97")] // 20
    [InlineData("number_chars(N, \"0'\\\\n\"), N == 10")] // 60
    [InlineData("number_chars(N, \"0'\\\\7\\\\\"), N == 7")] // 61
    [InlineData("number_chars(N, \"0'.\"), N == 46")] // 62
    [InlineData("number_chars(N, \"- 1\"), N == -1")] // 21
    [InlineData("number_chars(N, \"'-'1\"), N == -1")] // 54
    [InlineData("number_chars(N, \"/**/1\"), N == 1")] // 22
    [InlineData("number_chars(N, \"%\\n1\"), N == 1")] // 23
    [InlineData("number_chars(N, \"-%\\n0\"), N == 0")] // 74
    [InlineData("number_chars(N, \"- /**/1\"), N == -1")] // 57
    [InlineData("number_chars(N, \"'\\\\\\n-' 3\"), N == -3")] // 63
    [InlineData("number_chars(N, \"0x1\"), N == 1")] // 28
    [InlineData("number_chars(1, [C]), C == '1'")] // 31
    [InlineData("number_chars(10, [C, D]), [C, D] == ['1', '0']")] // 35
    [InlineData("number_chars(N, \"1.0e-8\"), number_chars(N, L), number_chars(N, L)")] // 78
    public void ReadsTheListAsTheReaderReadsANumber(string goal) => Assert.Equal("true", Outcome(goal));

    [Theory]
    [InlineData("number_chars(1, [C, D])")] // 32
    [InlineData("number_chars(0, [C, C])")] // 34
    [InlineData("number_chars(100, [C, D])")] // 36
    [InlineData("number_chars(1, ['.'|_])")] // 47
    public void APartialListComparesWithTheWrittenNumber(string goal) => Assert.Equal("false", Outcome(goal));

    [Theory]
    [InlineData("number_chars(1, \"a\")")] // 4
    [InlineData("number_chars(1, [])")] // 5
    [InlineData("number_chars(N, \"3 \")")] // 16
    [InlineData("number_chars(N, \"3.\")")] // 17
    [InlineData("number_chars(N, \"0'\")")] // 58
    [InlineData("number_chars(N, \"0''\")")]
    [InlineData("number_chars(N, \"0'\\\\0\")")]
    [InlineData("number_chars(N, \"0'\\n\")")] // 59
    [InlineData("number_chars(N, \"0'\\\\7\")")] // 71
    [InlineData("number_chars(N, \"-/**/1\")")] // 24
    [InlineData("number_chars(N, \"1e1\")")] // 25
    [InlineData("number_chars(N, \"1.0e\")")] // 26
    [InlineData("number_chars(N, \"0X1\")")] // 29
    [InlineData("number_chars(N, \"(0)\")")] // 73
    [InlineData("number_chars(N, \"0%0'\")")] // 84
    [InlineData("number_chars(N, \"+1\")")] // 48
    [InlineData("number_chars(N, \"'+'1\")")] // 50
    public void TextThatIsNotANumberIsASyntaxError(string goal) => Assert.Equal("syntax_error", Outcome(goal));

    // 0' followed by a line continuation: the escape denotes no character, so there is no code.
    [Theory]
    [InlineData("number_chars(N, ['0', '\\'', '\\\\', '\\n'])")]
    [InlineData("number_chars(N, ['0', '\\'', '\\\\', '\\n', a])")]
    [InlineData("number_codes(N, [48, 39, 92, 10])")]
    [InlineData("number_codes(N, [48, 39, 92, 10, 97])")]
    public void ACharacterCodeLiteralNeedsACharacter(string goal) => Assert.Equal("syntax_error", Outcome(goal));

    [Theory]
    [InlineData("number_chars(1, [[]])", "type_error(character,[])")] // 6
    [InlineData("number_chars(1, [' ', []])", "type_error(character,[])")] // 7
    [InlineData("number_chars(1, [0])", "type_error(character,0)")] // 8
    [InlineData("number_chars(1, [_, []])", "type_error(character,[])")] // 9
    [InlineData("number_chars(N, ['0'|_])", "instantiation_error")] // 11
    [InlineData("number_chars(N, [a|a])", "type_error(list,[a|a])")] // 13
    [InlineData("number_chars(1, 1)", "type_error(list,1)")] // 41
    [InlineData("number_chars(1, [a|2])", "type_error(list,[a|2])")] // 42
    [InlineData("number_chars(1, [_|2])", "type_error(list,[A|2])")] // 43
    [InlineData("number_chars(1, [[]|_])", "type_error(character,[])")] // 44
    [InlineData("number_chars(1+1, \"2\")", "type_error(number,1+1)")] // 53
    [InlineData("number_chars(N, \"9.9e999\")", "representation_error(max_float)")] // 82
    public void ReportsTheErrorsTheTableExpects(string goal, string expected) => Assert.Equal(expected, Outcome(goal));

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void CyclicListsRaiseAListTypeErrorWithTheCyclicCulprit(PrologLanguageMode mode)
    {
        var engine = new PrologEngine(mode);
        // Case 46, plus codes, bound numbers, and a cycle reached through a finite prefix.
        string[] goals =
        [
            "L = ['1'|L], number_chars(_, L)",
            "L = ['1'|L], number_chars(1, L)",
            "L = [49|L], number_codes(_, L)",
            "L = [49|L], number_codes(1, L)",
            "L = ['2', '3'|L], number_chars(_, ['1'|L])",
            "L = [50, 51|L], number_codes(_, [49|L])",
        ];
        foreach (var goal in goals)
        {
            Assert.True(
                engine
                    .Query(
                        $"catch(({goal}), error(type_error(list, C), _), Caught = yes), "
                            + "Caught == yes, nonvar(C), \\+ acyclic_term(C), var(L)"
                    )
                    .Prove(),
                goal
            );
        }
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void CharacterCodeQuotesAndOctalEscapesFollowIsoGrammar(PrologLanguageMode mode)
    {
        var engine = new PrologEngine(mode);
        string[] malformed = ["[48,39,39]", "[48,39,92,48]", "[48,39,92,48,56]", "[48,39,92,48,120]"];
        foreach (string codes in malformed)
        {
            foreach (string predicate in new[] { "number_chars", "number_codes" })
            {
                string list = predicate == "number_chars" ? "Chars" : codes;
                foreach (string number in new[] { "N", "0", "39" })
                {
                    string goal =
                        $"atom_codes(A, {codes}), atom_chars(A, Chars), "
                        + $"catch({predicate}({number}, {list}), error(syntax_error(_), _), Caught = yes), "
                        + "Caught == yes, var(N)";
                    Assert.True(engine.Query(goal).Prove(), goal);
                }
            }
        }

        foreach (
            (string codes, int expected) in new (string, int)[]
            {
                ("[48,39,39,39]", 39),
                ("[48,39,92,39]", 39),
                ("[48,39,92,48,92]", 0),
                ("[48,39,92,48,55,92]", 7),
                ("[48,39,92,92]", 92),
            }
        )
        {
            string goal =
                $"atom_codes(A, {codes}), atom_chars(A, Chars), "
                + $"number_chars(N, Chars), N == {expected}, number_chars({expected}, Chars), "
                + $"number_codes(M, {codes}), M == {expected}, number_codes({expected}, {codes})";
            Assert.True(engine.Query(goal).Prove(), goal);
        }
    }

    private static string Outcome(string goal) =>
        PrologTestHost.RunGoal(
            $"catch((({goal}) -> Outcome_ = true ; Outcome_ = false), error(Error_, _), Outcome_ = Error_), "
                + "( Outcome_ = syntax_error(_) -> write(syntax_error) ; copy_term(Outcome_, Shown_), numbervars(Shown_, 0, _), writeq(Shown_) )"
        );
}
