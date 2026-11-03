using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class RenameAnalysisTests
{
    [Fact]
    public void DeclarationsCallsAndHeadsUsePreciseTokensAndCallableArity()
    {
        const string text =
            ":- module(sample, [word//1]). :- multifile(word//1). :- discontiguous(word//1). "
            + "word(X) --> [X]. start --> word(a). run :- phrase(word(a), []), call(word(a), [], []), write(word(data)).";
        var analysis = Analyze(text);
        Assert.Null(analysis.BlockingReason);
        var sites = analysis.Sites.Where(site => site.Name == "word").ToArray();
        Assert.Equal(7, sites.Length);
        Assert.Equal(3, sites.Count(site => site.IsDeclaration));
        Assert.Single(sites, site => site.IsDefinition);
        Assert.All(
            sites,
            site =>
            {
                Assert.Equal(3, site.Arity);
                Assert.Equal("sample", site.Module);
                Assert.Equal("word", text.Substring(site.Range.Start, site.Range.Length));
            }
        );
    }

    [Theory]
    [InlineData("run(G) :- G.")]
    [InlineData("run(G) :- call(G).")]
    [InlineData("run(M) :- M:p.")]
    [InlineData("run :- missing.")]
    [InlineData("run :- assertz(p).")]
    [InlineData("run :- current_predicate(p/0).")]
    [InlineData("run :- functor(_, p, 0).")]
    [InlineData("run :- call(call(p), a).")]
    [InlineData(":- dynamic(p/0).")]
    [InlineData(":- op(500, xfy, joins).")]
    [InlineData(":- use_module('missing.pl').")]
    [InlineData(":- export(missing/0).")]
    [InlineData(":- unknown_directive(p).")]
    public void IncompleteOrReflectiveAnalysisBlocksRename(string suffix) =>
        Assert.NotNull(Analyze("p. " + suffix).BlockingReason);

    [Theory]
    [InlineData("p")]
    [InlineData("p_2")]
    public void PlainNamesAreAllowed(string name) => Assert.True(PrologRenameAnalysis.IsAllowedName(name));

    [Theory]
    [InlineData("")]
    [InlineData("Variable")]
    [InlineData("has space")]
    [InlineData("'quoted'")]
    [InlineData("λ")]
    [InlineData("write")]
    [InlineData("is")]
    [InlineData("goal_expansion")]
    public void UnsupportedOrReservedNamesAreRejected(string name) => Assert.False(PrologRenameAnalysis.IsAllowedName(name));

    [Fact]
    public void ImportCyclesAreRejectedEvenWhenEveryIndividualBindingResolves()
    {
        var analysis = PrologRenameAnalysis.Create(
            [Source(":- module(a, [p/0]). p. :- import(b, [])."), Source(":- module(b, [q/0]). q. :- import(a, []).")],
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Contains("Cyclic", analysis.BlockingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticNestedMetaGoalsRetainTheirSourceTargets()
    {
        var analysis = Analyze("p(a). run :- call(call(p(a))), once(p(a)), maplist(p, [a]), bagof(X, p(X), _).");
        Assert.Null(analysis.BlockingReason);
        Assert.Equal(5, analysis.Sites.Count(site => site.Name == "p"));
    }

    [Fact]
    public void MissingSourcesAndMalformedInputBlockRename()
    {
        var source = Source("p.");
        Assert.NotNull(PrologRenameAnalysis.Create([source], true, TestContext.Current.CancellationToken).BlockingReason);
        var loaded = PrologLintSourceLoader.Load(
            source.FileName,
            "p. :- include('missing.pl').",
            new BytecodeProgram(PrologLanguageMode.Modern),
            _ => throw new FileNotFoundException(),
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(
            PrologRenameAnalysis.Create([loaded.Source], cancellationToken: TestContext.Current.CancellationToken).BlockingReason
        );
    }

    private static PrologRenameAnalysis Analyze(string text) =>
        PrologRenameAnalysis.Create([Source(text)], cancellationToken: TestContext.Current.CancellationToken);

    private static PrologLintSource Source(string text) =>
        new(
            Path.Combine(Path.GetTempPath(), "rename-tests", "source.pl"),
            text,
            TermReader.ReadProgram(text).Clauses,
            DoubleQuotesMode.Codes,
            true
        );
}
