using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class BuiltinHoverTests
{
    [Fact]
    public void GoalPositionsRespectArityAndKeepDataAndHeadsOut()
    {
        const string text = "fact(member(a, [])). run :- member(a, []), write(member(a, [])), member(a), sort(0, @=<, [], _).";
        var sites = Analyze(text).Where(site => site.Documentation?.Signature != ",/2").ToArray();
        var help = sites.Where(site => site.Documentation is not null).Select(site => site.Documentation!).ToArray();
        Assert.Equal(["member/2", "write/1", "sort/4"], help.Select(item => item.Signature));
        Assert.Contains("duplicate retention", help[^1].Description, StringComparison.Ordinal);
        Assert.Equal(4, sites.Length);
        Assert.Null(sites.Single(site => text.Substring(site.Range.Start) == "member(a), sort(0, @=<, [], _).").Documentation);
    }

    [Theory]
    [InlineData("member(_, _). run :- member(a, []).")]
    [InlineData(":- dynamic(member/2). run :- member(a, []).")]
    [InlineData(":- multifile(member/2). run :- member(a, []).")]
    [InlineData(":- use_module('unavailable.pl', [member/2]). run :- member(a, []).")]
    [InlineData("run :- absent:member(a, []).")]
    [InlineData("run(M) :- M:member(a, []).")]
    public void ShadowedOrUncertainCallsDoNotReceiveBuiltinHelp(string text)
    {
        Assert.All(Analyze(text), site => Assert.Null(site.Documentation));
    }

    [Fact]
    public void ImportedDefinitionWinsButUnrelatedModuleAndArityStillResolve()
    {
        var library = Source("library.pl", ":- module(lists, [member/2]). member(a, []).");
        var client = Source(
            "client.pl",
            ":- module(client, []). :- use_module('library.pl', [member/2]). run :- member(a, []), user:member(a, []), write(a)."
        );
        var sites = PrologBuiltinHovers
            .Analyze([library, client], PrologLanguageMode.Modern, TestContext.Current.CancellationToken)
            .Where(site => site.Documentation?.Signature != ",/2")
            .ToArray();
        Assert.Null(sites[0].Documentation);
        Assert.Equal(["member/2", "write/1"], sites.Skip(1).Select(site => site.Documentation!.Signature));
    }

    [Fact]
    public void MetaClosuresInitializationAndDcgEscapesRetainCallableArity()
    {
        const string text =
            ":- initialization(write(ok)). run :- call(member(a), []), maplist(atom, []), bagof(X, Y^member(X,Y), _). g --> member(a), {write(ok)}, [write(data)].";
        var signatures = Analyze(text)
            .Where(site => site.Documentation is not null)
            .Select(site => site.Documentation!.Signature)
            .ToArray();
        Assert.Contains("call/2", signatures);
        Assert.Contains("maplist/2", signatures);
        Assert.Contains("atom/1", signatures);
        Assert.Equal(2, signatures.Count(signature => signature == "member/2"));
        Assert.Equal(2, signatures.Count(signature => signature == "write/1"));
    }

    [Fact]
    public void OverridingMetaPredicateDoesNotTurnDataIntoGoals()
    {
        var sites = Analyze("once(_). run :- once(write(data)).");
        Assert.Null(Assert.Single(sites).Documentation);
        var declared = Analyze(":- meta_predicate(invoke(0)). invoke(G) :- call(G). run :- invoke(write(ok)).");
        Assert.Contains(declared, site => site.Documentation?.Signature == "write/1");
    }

    [Fact]
    public void ModesRecoveryAndQuotedOperatorSpansArePreserved()
    {
        const string text = "run :- 'member'(a, []), X is 1+2. broken(. after :- write(ok).";
        var sites = Analyze(text, PrologLanguageMode.StrictIso);
        var member = sites.Single(site => site.Documentation?.Signature == "member/2");
        Assert.Equal("'member'", text.Substring(member.Range.Start, member.Range.Length));
        Assert.Contains("unavailable", member.Documentation!.Availability!, StringComparison.Ordinal);
        Assert.Null(sites.Single(site => site.Documentation?.Signature == "is/2").Documentation!.Availability);
        Assert.Contains(sites, site => site.Documentation?.Signature == "write/1");
        Assert.DoesNotContain(sites, site => text.Substring(site.Range.Start, site.Range.Length) == "+");
        Assert.Null(Assert.Single(Analyze("run :- '$collect_begin'.")).Documentation);
    }

    private static IReadOnlyList<PrologBuiltinHover> Analyze(string text, PrologLanguageMode mode = PrologLanguageMode.Modern) =>
        PrologBuiltinHovers.Analyze([Source("hover.pl", text)], mode, TestContext.Current.CancellationToken);

    private static PrologLintSource Source(string name, string text)
    {
        var program = new BytecodeProgram();
        var parsed = TermReader.ReadProgram(text, name, program.Operators, program.CharacterConversions, program.Flags);
        return new PrologLintSource(
            Path.Combine(Path.GetTempPath(), "hover-tests", name),
            text,
            parsed.Clauses,
            program.InitialDoubleQuotes,
            program.Flags.CodePointCharacters
        );
    }
}
