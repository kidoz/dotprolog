namespace DotProlog.Compiler.Tests;

/// <summary>ISO backquoted names, quoted control-character escapes, and runtime syntax errors.</summary>
public sealed class QuotedTokenTests
{
    [Fact]
    public void CompiledSourceReadsBackquotedNames()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.Run(
                """
                backquoted(`hello`).
                :- initialization((backquoted(hello), write(yes), nl)).
                """
            )
        );
    }

    [Fact]
    public void RuntimeTermInputReadsBackquotedNamesAndDeleteEscapes()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "read_term_from_atom('`hello`', hello, []), "
                    + "atom_codes(DeleteSource, [96,92,100,96]), "
                    + "read_term_from_atom(DeleteSource, Delete, []), "
                    + "atom_codes(Delete, [127]), write(yes), nl"
            )
        );
    }

    [Fact]
    public void RuntimeTermInputRejectsRawQuotedLayoutAsACatchableSyntaxError()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "atom_codes(BadSource, [39,114,97,119,9,108,97,121,111,117,116,39]), "
                    + "catch(read_term_from_atom(BadSource, _, []), "
                    + "error(syntax_error('DPL0011'), _), write(yes)), nl"
            )
        );
    }
}
