using System.Numerics;
using DotProlog.Runtime;

namespace DotProlog.Syntax.Tests;

/// <summary>
/// <c>number_chars/2</c> and <c>number_codes/2</c> read their list with the runtime's
/// <see cref="NumberReader"/>, which cannot call the lexer. Every text here must get the same answer
/// from it as from the lexer itself — the same number, or the same refusal — in both modes.
/// </summary>
public sealed class NumberReaderParityTests
{
    public static TheoryData<string, bool> Texts
    {
        get
        {
            string[] texts =
            [
                // The ISO conformity table for number_chars/2.
                "1.2",
                "1.20",
                "1.0E9",
                "1.0e9",
                "-0.0",
                "01",
                "010",
                "08",
                "0b11",
                "0o11",
                "0x11",
                "a",
                "",
                "3 ",
                "3.",
                " 1",
                "\n1",
                " 0'a",
                "0'",
                "0'\n",
                "0'\\n",
                "0'\\7\\",
                "0'\\7",
                "0'.",
                "- 1",
                "'-'1",
                "/**/1",
                "%\n1",
                "-%\n0",
                "- /**/1",
                "-/**/1",
                "'\\\n-' 3",
                "1e1",
                "1.0e",
                "1.0ee",
                "0x1",
                "0X1",
                "1E1",
                "(0)",
                "0%0'",
                "1.0e-8",
                "+1",
                "+ 1",
                "'+'1",
                "9.9e999",
                "0.1e-999",
                // A line continuation after 0' denotes no character.
                "0'\\\n",
                "0'\\\na",
                // Character codes, escapes, and quotes.
                "0'''",
                "0''",
                "0' ",
                "0'\t",
                "0'\\x41\\",
                "0'\\u0041",
                "0'\\U0001F600",
                "0'😀",
                "0'\\z",
                "0'\\e",
                "0'\\d",
                "0'\\0\\",
                "0'\\\\",
                "0'\\'",
                "0'\\xD800\\",
                "0'\\x110000\\",
                // A quoted minus spelled with escapes, and names that are not a lone minus.
                "'\\x2d\\'1",
                "'\\55\\'1",
                "'-",
                "-",
                "--1",
                "- -1",
                "'- '1",
                "'-''1",
                "'\\q'1",
                // Rationals, which strict ISO mode does not read.
                "1r3",
                "-1r3",
                "2r4",
                "1r0",
                "1r",
                "r3",
                // Integers past the fixnum range, exponents, and what is not a number at all.
                "99999999999999999999",
                "-99999999999999999999",
                "0xFFFFFFFFFFFFFFFFFF",
                "1.5e+3",
                "1.5e+",
                "1.5e-",
                "1_000",
                "0b",
                "0x",
                "0b2",
                "/*",
                "1/**/",
                "1%c",
                "1.0Inf",
                "inf",
                "nan",
                " \t\r\n 7",
                "- \n 7",
            ];

            var data = new TheoryData<string, bool>();
            foreach (var text in texts)
            {
                data.Add(text, true);
                data.Add(text, false);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void TheRuntimeReadsTheListAsTheLexerDoes(string text, bool modern)
    {
        PrologFlags flags = new BytecodeProgram(modern ? PrologLanguageMode.Modern : PrologLanguageMode.StrictIso).Flags;

        SyntaxTerm? expected = TermReader.ReadNumber(text, flags, out IReadOnlyList<Diagnostic> diagnostics);
        NumberReader.Outcome outcome = NumberReader.Read(
            text,
            flags.CodePointCharacters,
            flags.RationalLiterals,
            out PrologNumber number
        );

        if (expected is null)
        {
            var overflow = diagnostics.Any(diagnostic => diagnostic.Id == DiagnosticIds.FloatOverflow);
            Assert.Equal(overflow ? NumberReader.Outcome.FloatOverflow : NumberReader.Outcome.NotANumber, outcome);
            return;
        }

        Assert.Equal(NumberReader.Outcome.Number, outcome);
        switch (expected)
        {
            case IntegerTerm integer:
                Assert.True(number.IsInteger);
                Assert.Equal(new BigInteger(integer.Value), number.Big);
                break;
            case BigIntegerTerm big:
                Assert.True(number.IsInteger);
                Assert.Equal(big.Value, number.Big);
                break;
            case RationalTerm rational:
                Assert.Equal(rational.Numerator * number.Denominator, number.Numerator * rational.Denominator);
                break;
            case FloatTerm real:
                Assert.True(number.IsFloat);
                Assert.Equal(BitConverter.DoubleToInt64Bits(real.Value), BitConverter.DoubleToInt64Bits(number.Real));
                break;
            default:
                Assert.Fail($"Unexpected term {expected}.");
                break;
        }
    }
}
