using DotProlog.Runtime;

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
    public void CompiledAndRuntimeInputReadIsoOctalEscapes()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.Run(
                """
                octal('a\123\b').
                :- initialization((octal('aSb'), write(yes), nl)).
                """
            )
        );
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                "atom_codes(Source, [39,97,92,49,50,51,92,98,39]), " + "read_term_from_atom(Source, aSb, []), write(yes), nl"
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

    [Fact]
    public void StreamingTermInputIgnoresTerminatorsInsideBackquotedNames()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Input = new StringReader("`inside.with.dot`. next."), Output = output };

        engine.ConsultOrThrow(
            """
            :- initialization((read(First), read(Second), write([First,Second]), nl)).
            """,
            "backquoted-stream.pl"
        );

        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("[inside.with.dot,next]\n", output.ToString());
    }
}
