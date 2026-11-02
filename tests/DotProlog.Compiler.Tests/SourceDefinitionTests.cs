using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class SourceDefinitionTests
{
    [Fact]
    public void ReferenceMetadataDistinguishesHeadsFromOrdinaryAndDcgMetaCalls()
    {
        const string text = "p(a). p(b). run :- p(a), call(p, b), phrase(word(a), []). word(X) --> [X].";
        var sites = Analyze(Source("references.pl", text));
        var predicate = sites.Where(site => site.Name == "p").ToArray();
        Assert.Equal(2, predicate.Count(site => site.IsDefinition));
        Assert.Equal(2, predicate.Count(site => !site.IsDefinition));
        Assert.All(predicate, site => Assert.Equal(2, site.Definitions.Count));
        var grammar = sites.Where(site => site.Name == "word").ToArray();
        Assert.Single(grammar, site => site.IsDefinition);
        Assert.Single(grammar, site => !site.IsDefinition && site.Arity == 3);
    }

    [Fact]
    public void CallsAndHeadsSelectEveryClauseByArityAndExcludeData()
    {
        const string text = "p(a). p(b). p(_, _). run :- p(a), write(p(data)), p(a, b), missing, write(ok).";
        var sites = Analyze(Source("main.pl", text));
        Assert.Equal(2, At(sites, text.IndexOf("p(a)", StringComparison.Ordinal)).Definitions.Count);
        Assert.Equal(2, At(sites, text.IndexOf("p(a),", StringComparison.Ordinal)).Definitions.Count);
        Assert.Single(At(sites, text.IndexOf("p(a, b)", StringComparison.Ordinal)).Definitions);
        Assert.DoesNotContain(sites, site => site.Range.Start == text.IndexOf("p(data)", StringComparison.Ordinal));
        Assert.Empty(At(sites, text.IndexOf("missing", StringComparison.Ordinal)).Definitions);
        Assert.Empty(At(sites, text.LastIndexOf("write", StringComparison.Ordinal)).Definitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportsReexportsQualificationAndUserFallbackResolveToImplementation(bool reversed)
    {
        var library = Source("library.pl", ":- module(library, [item/1]). item(one). secret.");
        var facade = Source("facade.pl", ":- module(facade, []). :- reexport(library, [item/1]).");
        var client = Source(
            "client.pl",
            ":- module(client, []). :- use_module('facade.pl'). run :- item(X), fallback, secret, library:secret, unknown:item(X)."
        );
        var user = Source("user.pl", "fallback.");
        var sources = reversed ? new[] { client, facade, library, user } : new[] { library, facade, client, user };
        var sites = Analyze(sources).Where(site => site.FileName == client.FileName).ToArray();
        Assert.Equal(
            library.FileName,
            Assert.Single(At(sites, client.Source.IndexOf("item(X)", StringComparison.Ordinal)).Definitions).FileName
        );
        Assert.Equal(
            user.FileName,
            Assert.Single(At(sites, client.Source.IndexOf("fallback", StringComparison.Ordinal)).Definitions).FileName
        );
        Assert.Empty(At(sites, client.Source.IndexOf("secret,", StringComparison.Ordinal)).Definitions);
        Assert.Equal("library", At(sites, client.Source.LastIndexOf("secret", StringComparison.Ordinal)).Module);
        Assert.Empty(At(sites, client.Source.LastIndexOf("item(X)", StringComparison.Ordinal)).Definitions);
    }

    [Fact]
    public void DcgCallsUseLoweredArityAndDoNotTreatTerminalsAsGoals()
    {
        const string text =
            "word(X) --> [X]. plain. start --> word(a), {plain}, [word(data)]. run :- phrase(word(a), []), call(word(a), [], []).";
        var sites = Analyze(Source("dcg.pl", text));
        foreach (
            int offset in new[]
            {
                text.IndexOf("word(a)", StringComparison.Ordinal),
                text.IndexOf("word(a), []", StringComparison.Ordinal),
                text.LastIndexOf("word(a)", StringComparison.Ordinal),
            }
        )
        {
            var site = At(sites, offset);
            Assert.Equal(3, site.Arity);
            Assert.True(Assert.Single(site.Definitions).IsNonterminal);
            Assert.Equal(1, site.Definitions[0].Arity);
        }
        Assert.Single(At(sites, text.IndexOf("plain}", StringComparison.Ordinal)).Definitions);
        Assert.DoesNotContain(sites, site => site.Range.Start == text.IndexOf("word(data)", StringComparison.Ordinal));
    }

    [Fact]
    public void DcgMetaCallsNavigateToNonterminalsWithoutInventingOrdinaryArity()
    {
        const string text = "word(X) --> [X]. start --> call(word(a)), phrase(word(b)).";
        var sites = Analyze(Source("meta-dcg.pl", text));
        Assert.True(Assert.Single(At(sites, text.IndexOf("word(a)", StringComparison.Ordinal)).Definitions).IsNonterminal);
        Assert.True(Assert.Single(At(sites, text.IndexOf("word(b)", StringComparison.Ordinal)).Definitions).IsNonterminal);
    }

    [Fact]
    public void MetaClosuresInitializationAndOverriddenMetaNamesRespectGoalPositions()
    {
        const string text =
            ":- meta_predicate(invoke(0)). invoke(G) :- call(G). target(_). :- initialization(target(a)). run :- invoke(target(a)), call(target, a), maplist(target, []), bagof(X, Y^target(X), _).";
        var sites = Analyze(Source("meta.pl", text));
        Assert.Equal(6, sites.Count(site => site.Name == "target" && site.Definitions.Count == 1));
        const string shadow = "once(_). target. run :- once(target).";
        var overridden = Analyze(Source("shadow.pl", shadow));
        Assert.DoesNotContain(overridden, site => site.Range.Start == shadow.LastIndexOf("target", StringComparison.Ordinal));
    }

    [Fact]
    public void MultifileAndDynamicPredicatesUseAvailableSourceBodies()
    {
        var one = Source("one.pl", ":- multifile(p/1). p(one). :- dynamic(empty/1). run :- p(X), empty(X).");
        var two = Source("two.pl", ":- multifile(p/1). p(two).");
        var sites = Analyze(one, two).Where(site => site.FileName == one.FileName).ToArray();
        Assert.Equal(2, At(sites, one.Source.IndexOf("p(X)", StringComparison.Ordinal)).Definitions.Count);
        Assert.Empty(At(sites, one.Source.IndexOf("empty(X)", StringComparison.Ordinal)).Definitions);
    }

    [Fact]
    public void QuotedAndOperatorNamesUseNameTokensAndRecoverAfterIncompleteClauses()
    {
        const string text =
            ":- op(500, xfx, joins). 'has space'(a). a joins b. run :- 'has space'(a), a joins b. broken(. after :- 'has space'(b).";
        var sites = Analyze(Source("quoted.pl", text));
        var quoted = At(sites, text.LastIndexOf("'has space'", StringComparison.Ordinal));
        Assert.Equal("'has space'", text.Substring(quoted.Range.Start, quoted.Range.Length));
        Assert.Equal(
            "'has space'",
            text.Substring(Assert.Single(quoted.Definitions).SelectionRange.Start, quoted.Definitions[0].SelectionRange.Length)
        );
        Assert.Single(At(sites, text.LastIndexOf("joins", StringComparison.Ordinal)).Definitions);
    }

    [Fact]
    public void VariableQualifiersAndUnknownImportsDoNotInventTargetsButLocalDefinitionsWin()
    {
        const string text = ":- use_module('missing.pl'). local. run(M) :- M:local, local, member(a, []).";
        var sites = Analyze(Source("unknown.pl", text));
        Assert.Empty(At(sites, text.IndexOf("local,", StringComparison.Ordinal)).Definitions);
        Assert.Single(At(sites, text.LastIndexOf("local,", StringComparison.Ordinal)).Definitions);
        Assert.Empty(At(sites, text.IndexOf("member", StringComparison.Ordinal)).Definitions);
    }

    private static PrologDefinitionSite At(IEnumerable<PrologDefinitionSite> sites, int offset) =>
        Assert.Single(sites, site => site.Range.Start == offset);

    private static IReadOnlyList<PrologDefinitionSite> Analyze(params PrologLintSource[] sources) =>
        PrologSourceDefinitions.Analyze(sources, TestContext.Current.CancellationToken);

    private static PrologLintSource Source(string name, string text) =>
        new(
            Path.Combine(Path.GetTempPath(), "definition-tests", name),
            text,
            TermReader.ReadProgram(text, name).Clauses,
            DoubleQuotesMode.Codes,
            true
        );
}
