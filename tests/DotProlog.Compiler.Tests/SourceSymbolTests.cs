using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class SourceSymbolTests
{
    [Fact]
    public void SymbolsReuseModuleContextAndPhysicalIncludeOwnership()
    {
        string root = Path.Combine(Path.GetTempPath(), "symbols", "root.pl");
        var loaded = PrologLintSourceLoader.Load(
            root,
            ":- module(m, []). :- include('child.pl'). local. :- initialization(halt(42)).",
            new BytecodeProgram(),
            _ => "p(a). p(X) --> [X].",
            TestContext.Current.CancellationToken
        );
        var symbols = PrologSourceSymbols.Analyze([loaded.Source], TestContext.Current.CancellationToken);
        Assert.Equal(3, symbols.Count);
        Assert.All(symbols, symbol => Assert.Equal("m", symbol.Module));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(root)!, "child.pl"), symbols[0].FileName);
        Assert.Equal(root, symbols[2].FileName);
        Assert.Equal(1, symbols[0].Arity);
        Assert.Equal(1, symbols[1].Arity);
        Assert.False(symbols[0].IsNonterminal);
        Assert.True(symbols[1].IsNonterminal);
    }

    [Fact]
    public void IsoBodiesSemicontextsAndIncompleteClausesPreserveSourceIdentity()
    {
        const string text =
            ":- module(m). :- export(p/0). :- end_module(m). :- body(m). p. g(X), [X] --> [X]. :- end_body(m). outside. broken(. recovered.";
        var parsed = TermReader.ReadProgram(text);
        var source = new PrologLintSource("outline.pl", text, parsed.Clauses, DoubleQuotesMode.Chars, true);
        var symbols = PrologSourceSymbols.Analyze([source], TestContext.Current.CancellationToken);
        Assert.NotEmpty(parsed.Diagnostics);
        Assert.Equal(["p", "g", "outside", "recovered"], symbols.Select(symbol => symbol.Name));
        Assert.Equal(["m", "m", "user", "user"], symbols.Select(symbol => symbol.Module));
        Assert.Equal(1, symbols[1].Arity);
        Assert.True(symbols[1].IsNonterminal);
        Assert.Equal("g", text.Substring(symbols[1].SelectionRange.Start, symbols[1].SelectionRange.Length));
    }
}
