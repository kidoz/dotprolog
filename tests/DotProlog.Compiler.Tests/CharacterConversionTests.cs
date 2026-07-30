using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>ISO character-conversion state, enumeration, term input, directives, and errors.</summary>
public sealed class CharacterConversionTests
{
    [Fact]
    public void AddsEnumeratesAndRemovesMappings()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "char_conversion(a, b), current_char_conversion(a, b), "
                    + "\\+ current_char_conversion(Same, Same), var(Same), "
                    + "char_conversion(a, a), \\+ current_char_conversion(a, _), write(yes), nl"
            )
        );
    }

    [Fact]
    public void EnumerationRetainsItsStartingSnapshotAcrossMutation()
    {
        Assert.Equal(
            "[a-x,b-y]\n",
            PrologTestHost.RunGoal(
                "char_conversion(a, x), char_conversion(b, y), "
                    + "findall(I-O, (current_char_conversion(I, O), "
                    + "( I == a -> char_conversion(b, z) ; true )), Pairs), "
                    + "write(Pairs), nl"
            )
        );
    }

    [Fact]
    public void FlagGatesTermInputWithoutDiscardingMappings()
    {
        Assert.Equal(
            "[z,x,z]\n",
            PrologTestHost.RunGoal(
                "char_conversion(z, x), "
                    + "read_term_from_atom(z, OffBefore, []), "
                    + "set_prolog_flag(char_conversion, on), "
                    + "read_term_from_atom(z, On, []), "
                    + "set_prolog_flag(char_conversion, off), "
                    + "read_term_from_atom(z, OffAfter, []), "
                    + "write([OffBefore,On,OffAfter]), nl"
            )
        );
    }

    [Fact]
    public void QuotedAtomsAndStringsRemainUnconverted()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "char_code(SingleQuote, 39), char_code(DoubleQuote, 34), char_code(Backquote, 96), "
                    + "char_conversion(z, x), char_conversion(SingleQuote, x), "
                    + "char_conversion(DoubleQuote, x), char_conversion(Backquote, x), "
                    + "set_prolog_flag(char_conversion, on), "
                    + "atom_codes(SingleQuoted, [39,122,39]), "
                    + "read_term_from_atom(SingleQuoted, z, []), "
                    + "set_prolog_flag(double_quotes, atom), "
                    + "atom_codes(DoubleQuoted, [34,122,34]), "
                    + "read_term_from_atom(DoubleQuoted, z, []), "
                    + "atom_codes(Backquoted, [96,122,96]), "
                    + "read_term_from_atom(Backquoted, z, []), "
                    + "write(yes), nl"
            )
        );
    }

    [Fact]
    public void CharacterCodeLiteralPayloadRemainsUnconverted()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "char_conversion(a, x), set_prolog_flag(char_conversion, on), "
                    + "atom_codes(Literal, [48,39,97]), "
                    + "read_term_from_atom(Literal, Code, []), "
                    + "Code =:= 97, write(yes), nl"
            )
        );
    }

    [Fact]
    public void ConversionCanChangeLexicalCategoryAndCreateLayout()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "char_conversion(n, '1'), char_conversion('#', ' '), "
                    + "set_prolog_flag(char_conversion, on), "
                    + "read_term_from_atom('n#', 1, []), "
                    + "write(yes), nl"
            )
        );
    }

    [Fact]
    public void PrimitiveCharacterInputRemainsUnconverted()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = new StringReader("z") };

        engine.ConsultOrThrow(
            """
            :- initialization((
                char_conversion(z, x),
                set_prolog_flag(char_conversion, on),
                get_char(Character),
                write(Character), nl
            )).
            """,
            "primitive.pl"
        );

        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("z\n", output.ToString());
    }

    [Fact]
    public void SourceDirectivesAffectTheFollowingClause()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.Run(
                """
                :- char_conversion(z, x).
                :- set_prolog_flag(char_conversion, on).
                fizz.
                :- set_prolog_flag(char_conversion, off).
                :- initialization((fixx, write(yes), nl)).
                """
            )
        );
    }

    [Fact]
    public void ConvertedTerminatorsAreRecognizedByStreamingTermInput()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = new StringReader("hello#") };

        engine.ConsultOrThrow(
            """
            :- initialization((
                char_conversion('#', '.'),
                set_prolog_flag(char_conversion, on),
                read(Term),
                write(Term), nl
            )).
            """,
            "terminator.pl"
        );

        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("hello\n", output.ToString());
    }

    [Fact]
    public void HostGoalsUseTheSameConversionState()
    {
        var engine = new PrologEngine();
        engine.ConsultOrThrow("fixx.", "host.pl");

        Assert.Equal(RunResult.Success, engine.RunGoal("char_conversion(z, x)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("set_prolog_flag(char_conversion, on)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("fizz", out _));
    }

    [Fact]
    public void MappingStateIsIsolatedBetweenEngines()
    {
        var first = new PrologEngine();
        var second = new PrologEngine();

        Assert.Equal(RunResult.Success, first.RunGoal("char_conversion(z, x)", out _));
        Assert.Equal(RunResult.Success, first.RunGoal("current_char_conversion(z, x)", out _));
        Assert.Equal(RunResult.Failure, second.RunGoal("current_char_conversion(z, _)", out _));
    }

    [Theory]
    [InlineData("char_conversion(_, a)", "instantiation_error")]
    [InlineData("char_conversion(a, _)", "instantiation_error")]
    [InlineData("char_conversion(ab, a)", "representation_error(character)")]
    [InlineData("char_conversion(a, 1)", "representation_error(character)")]
    [InlineData("current_char_conversion(ab, _)", "type_error(character,ab)")]
    [InlineData("current_char_conversion(_, 1)", "type_error(character,1)")]
    public void ReportsIsoCharacterConversionErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
}
