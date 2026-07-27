using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Modules: what a file exports, what it keeps to itself, and how a call inside one is resolved.
/// </summary>
public sealed class ModuleTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-modules-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string source)
    {
        string path = System.IO.Path.Combine(_directory, name);
        File.WriteAllText(path, source);
        return path.Replace("\\", "/", StringComparison.Ordinal);
    }

    /// <summary>Consults a file and runs its goals, so that use_module has a directory to look in.</summary>
    private static string RunFile(string path)
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultFile(path);
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        return output.ToString();
    }

    [Fact]
    public void APredicateTheExportListOmitsIsLocalToItsModule()
    {
        // The point of the whole feature: shapes has its own helper/1 and so does the file that uses
        // it, and neither can see the other's.
        Write(
            "shapes.pl",
            """
            :- module(shapes, [describe/1]).

            describe(N) :- helper(N).

            helper(N) :- write(from_shapes(N)).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(shapes).

            helper(N) :- write(from_main(N)).

            :- initialization((describe(1), nl, helper(2), nl)).
            """
        );

        Assert.Equal("from_shapes(1)\nfrom_main(2)\n", RunFile(main));
    }

    [Fact]
    public void AnExportedPredicateIsCallableByItsPlainName()
    {
        string path = Write(
            "exported.pl",
            """
            :- module(exported, [square/2]).

            square(N, S) :- S is N * N.

            :- initialization((square(3, S), write(S), nl)).
            """
        );

        Assert.Equal("9\n", RunFile(path));
    }

    [Fact]
    public void ALocalPredicateIsNotCallableByItsPlainName()
    {
        string path = Write(
            "hidden.pl",
            """
            :- module(hidden, [visible/0]).

            visible :- write(yes).

            secret :- write(no).

            :- initialization(catch(secret, error(existence_error(procedure, PI), _), (write(PI), nl))).
            """
        );

        Assert.Equal("secret/0\n", RunFile(path));
    }

    [Fact]
    public void QualifyingReachesAPredicateTheExportListOmits()
    {
        Write(
            "library.pl",
            """
            :- module(library, []).

            internal(reached).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(library).
            :- initialization((library:internal(X), write(X), nl)).
            """
        );

        Assert.Equal("reached\n", RunFile(main));
    }

    [Fact]
    public void ImportingByNameTakesOnlyWhatIsListed()
    {
        Write(
            "pair.pl",
            """
            :- module(pair, [first/1, second/1]).

            first(1).
            second(2).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(pair, [first/1]).

            :- initialization((first(A), write(A), nl)).
            """
        );

        Assert.Equal("1\n", RunFile(main));
    }

    [Fact]
    public void AModuleSeesTheStandardLibrary()
    {
        string path = Write(
            "uses-library.pl",
            """
            :- module(uses_library, [go/0]).

            go :- length([a, b, c], N), atom_length(hello, M), Total is N + M, write(Total), nl.

            :- initialization(go).
            """
        );

        Assert.Equal("8\n", RunFile(path));
    }

    [Fact]
    public void AClosurePassedToALibraryPredicateResolvesInItsOwnModule()
    {
        // maplist/3 lives in the library, so the closure it calls has to carry the module it came
        // from. Without that, double/2 would be looked for in user and not found.
        string path = Write(
            "closures.pl",
            """
            :- module(closures, [go/0]).

            go :- maplist(double, [1, 2, 3], L), write(L), nl.

            double(X, Y) :- Y is X * 2.

            :- initialization(go).
            """
        );

        Assert.Equal("[2,4,6]\n", RunFile(path));
    }

    [Fact]
    public void AGoalPassedToFindallResolvesInItsOwnModule()
    {
        string path = Write(
            "collects.pl",
            """
            :- module(collects, [go/0]).

            go :- findall(X, item(X), L), write(L), nl.

            item(a).
            item(b).

            :- initialization(go).
            """
        );

        Assert.Equal("[a,b]\n", RunFile(path));
    }

    [Fact]
    public void AGoalBuiltAtRunTimeResolvesInItsOwnModule()
    {
        string path = Write(
            "meta.pl",
            """
            :- module(meta, [go/0]).

            go :- G = local(x), call(G).

            local(X) :- write(ran(X)), nl.

            :- initialization(go).
            """
        );

        Assert.Equal("ran(x)\n", RunFile(path));
    }

    [Fact]
    public void ADeclaredMetaPredicateResolvesItsGoalArgument()
    {
        Write(
            "runner.pl",
            """
            :- module(runner, [twice/1]).
            :- meta_predicate twice(0).

            twice(G) :- call(G), call(G).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(runner).

            :- initialization(twice(write(x))).
            """
        );

        Assert.Equal("xx", RunFile(main));
    }

    [Fact]
    public void ADynamicPredicateInAModuleIsLocalToIt()
    {
        Write(
            "store.pl",
            """
            :- module(store, [put/1, all/1]).
            :- dynamic fact/1.

            put(X) :- assertz(fact(X)).
            all(L) :- findall(X, fact(X), L).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(store).
            :- dynamic fact/1.

            :- initialization((assertz(fact(mine)), put(theirs), all(L), write(L), nl, findall(X, fact(X), M), write(M), nl)).
            """
        );

        Assert.Equal("[theirs]\n[mine]\n", RunFile(main));
    }

    [Fact]
    public void AnExportedDynamicPredicateIsOneDatabaseUnderBothNames()
    {
        // Aliasing an exported dynamic predicate has to share the clause list, not just the entry
        // address, or asserting through the plain name would build a second, invisible database.
        Write(
            "notes.pl",
            """
            :- module(notes, [note/1, all_notes/1]).
            :- dynamic note/1.

            all_notes(L) :- findall(X, note(X), L).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(notes).

            :- initialization((
                 assertz(note(one)),
                 notes:assertz(note(two)),
                 all_notes(L), write(L), nl,
                 findall(X, note(X), M), write(M), nl)).
            """
        );

        Assert.Equal("[one,two]\n[one,two]\n", RunFile(main));
    }

    [Fact]
    public void AGrammarRuleIsExportedAsItsCompiledPredicate()
    {
        Write(
            "grammar.pl",
            """
            :- module(grammar, [digits//1]).

            digits([D|T]) --> [D], digits(T).
            digits([D])   --> [D].
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(grammar).

            :- initialization((phrase(digits(D), [1, 2]), write(D), nl)).
            """
        );

        Assert.Equal("[1,2]\n", RunFile(main));
    }

    [Fact]
    public void AFileIsLoadedOnlyOnceHoweverManyTimesItIsUsed()
    {
        Write(
            "counter.pl",
            """
            :- module(counter, [tick/0]).

            tick :- write(tick).

            :- initialization((write(loaded), nl)).
            """
        );

        Write(
            "middle.pl",
            """
            :- module(middle, [go/0]).
            :- use_module(counter).

            go :- tick.
            """
        );

        string main = Write(
            "main.pl",
            """
            :- use_module(counter).
            :- use_module(middle).

            :- initialization((go, nl)).
            """
        );

        Assert.Equal("loaded\ntick\n", RunFile(main));
    }

    [Fact]
    public void AFileWithNoModuleDeclarationKeepsItsPlainNames()
    {
        string path = Write(
            "plain.pl",
            """
            helper(X) :- write(X).

            :- initialization((helper(unqualified), nl)).
            """
        );

        Assert.Equal("unqualified\n", RunFile(path));
    }

    [Fact]
    public void AMissingModuleFileIsReported()
    {
        string path = Write("missing.pl", ":- use_module(nowhere).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.ModuleNotFound);
    }

    [Fact]
    public void AMalformedExportListIsReported()
    {
        string path = Write("bad.pl", ":- module(bad, [oops]).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleDeclaration);
    }
}
