namespace DotProlog.Compiler.Tests;

/// <summary>Implementation-defined extended character classes at source and runtime reader boundaries.</summary>
public sealed class LexicalCharacterTests
{
    [Fact]
    public void CompiledSourceTreatsExtendedTitlecaseLettersAsAtoms()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.Run(
                """
                extended_atom(ǅelta).
                :- initialization((extended_atom(ǅelta), write(yes), nl)).
                """
            )
        );
    }

    [Fact]
    public void RuntimeTermInputRejectsNonAsciiDigitStartsAsCatchableSyntaxErrors()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "atom_codes(Source, [1633]), "
                    + "catch(read_term_from_atom(Source, _, []), "
                    + "error(syntax_error('DPL0001'), _), write(yes)), nl"
            )
        );
    }
}
