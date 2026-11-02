using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class CompletionIndexTests
{
    [Fact]
    public void LocalImportsAndExplicitModulesUseCompilerVisibility()
    {
        var library = Source("library.pl", ":- module(lists, [member/2]). member(_, _). secret.");
        var client = Source("client.pl", ":- module(client, []). :- use_module('library.pl', [member/2]). local. run :- mem");
        var index = PrologCompletionIndex.Create(
            [library, client],
            PrologLanguageMode.Modern,
            TestContext.Current.CancellationToken
        );
        var candidates = index.At(client.FileName, client.Source.Length);
        Assert.Equal("lists", Assert.Single(candidates, item => item.Name == "member" && item.Arity == 2).Module);
        Assert.Contains(candidates, item => item.Name == "local" && item.Module == "client");
        Assert.DoesNotContain(candidates, item => item.Name == "secret");
        Assert.Contains(index.At(client.FileName, client.Source.Length, "lists"), item => item.Name == "secret");
        Assert.Empty(index.At(client.FileName, client.Source.Length, "unknown"));
    }

    [Fact]
    public void DeclarationsOverrideBuiltinsAndStrictModeFiltersOnlyPredefinedCandidates()
    {
        var plain = Source("plain.pl", "run :- mem");
        var strict = PrologCompletionIndex.Create([plain], PrologLanguageMode.StrictIso, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(strict.At(plain.FileName, 0), item => item.Name == "member");
        Assert.Contains(strict.At(plain.FileName, 0), item => item.Name == "call" && item.Arity == 1);
        var custom = Source("custom.pl", ":- dynamic(member/2). :- multifile(write/1). run :- mem");
        var items = PrologCompletionIndex
            .Create([custom], PrologLanguageMode.StrictIso, TestContext.Current.CancellationToken)
            .At(custom.FileName, custom.Source.Length);
        Assert.Equal("user", Assert.Single(items, item => item.Name == "member" && item.Arity == 2).Module);
        Assert.Null(Assert.Single(items, item => item.Name == "write" && item.Arity == 1).Documentation);
        Assert.DoesNotContain(items, item => item.Name.StartsWith('$'));
    }

    [Fact]
    public void UnresolvedImportsDoNotInventBuiltinResolution()
    {
        var source = Source("client.pl", ":- use_module('missing.pl', [member/2]). run :- mem");
        var items = PrologCompletionIndex
            .Create([source], PrologLanguageMode.Modern, TestContext.Current.CancellationToken)
            .At(source.FileName, source.Source.Length);
        Assert.DoesNotContain(items, item => item.Name == "member" && item.Arity == 2);
        Assert.Empty(items);
    }

    [Fact]
    public void ModuleTransitionsAndIncludeOwnershipSurviveIncompleteTail()
    {
        var source = Source("root.pl", ":- module(a, []). first. :- module(b, []). second. run :- sec");
        var index = PrologCompletionIndex.Create([source], PrologLanguageMode.Modern, TestContext.Current.CancellationToken);
        Assert.Contains(index.At(source.FileName, source.Source.Length), item => item.Name == "second");
        Assert.DoesNotContain(index.At(source.FileName, source.Source.Length), item => item.Name == "first");
        var child = Source("child.pl", "child. run :- ch");
        var root = Source("root.pl", ":- module(parent, []). parent.");
        var expanded = root with
        {
            Clauses = [.. root.Clauses, .. child.Clauses],
            ClauseFileNames = [root.FileName, root.FileName, child.FileName],
        };
        var included = PrologCompletionIndex
            .Create([expanded], PrologLanguageMode.Modern, TestContext.Current.CancellationToken)
            .At(child.FileName, child.Source.Length);
        Assert.Contains(included, item => item.Name == "parent" && item.Module == "parent");
        Assert.Contains(included, item => item.Name == "child" && item.Module == "parent");
    }

    private static PrologLintSource Source(string file, string text) =>
        new(
            Path.Combine(Path.GetTempPath(), "completion-tests", file),
            text,
            TermReader.ReadProgram(text, file).Clauses,
            DoubleQuotesMode.Codes,
            true
        );
}
