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
        var path = System.IO.Path.Combine(_directory, name);
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

        var main = Write(
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
        var path = Write(
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
        var path = Write(
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

        var main = Write(
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

        var main = Write(
            "main.pl",
            """
            :- use_module(pair, [first/1]).

            :- initialization((first(A), write(A), nl)).
            """
        );

        Assert.Equal("1\n", RunFile(main));
    }

    [Fact]
    public void ASelectedImportMustBeExported()
    {
        Write(
            "library.pl",
            """
            :- module(library, [visible/0]).
            visible.
            hidden.
            """
        );
        var main = Write("main.pl", ":- use_module(library, [hidden/0]).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(main);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleImport);
    }

    [Fact]
    public void ASelectedImportMustContainPredicateIndicators()
    {
        Write(
            "library.pl",
            """
            :- module(library, [visible/0]).
            visible.
            """
        );
        var main = Write("main.pl", ":- use_module(library, [visible]).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(main);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleImport);
    }

    [Fact]
    public void ConflictingImportsAreReported()
    {
        Write("first.pl", ":- module(first, [item/1]).\nitem(first).\n");
        Write("second.pl", ":- module(second, [item/1]).\nitem(second).\n");
        var main = Write(
            "main.pl",
            """
            :- module(main, []).
            :- use_module(first).
            :- use_module(second).
            """
        );

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(main);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleImport);
    }

    [Fact]
    public void AModuleDeclarationMustBeFirst()
    {
        var path = Write(
            "late.pl",
            """
            before.
            :- module(late, [before/0]).
            """
        );

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleDeclaration);
    }

    [Fact]
    public void AModuleTextCannotDeclareTwoModules()
    {
        var path = Write(
            "duplicate.pl",
            """
            :- module(first, []).
            :- module(second, []).
            """
        );

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleDeclaration);
    }

    [Fact]
    public void AModuleSeesTheStandardLibrary()
    {
        var path = Write(
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
        var path = Write(
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
        var path = Write(
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
        var path = Write(
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

        var main = Write(
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

        var main = Write(
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

        var main = Write(
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

        var main = Write(
            "main.pl",
            """
            :- use_module(grammar).

            :- initialization((phrase(digits(D), [1, 2]), write(D), nl)).
            """
        );

        Assert.Equal("[1,2]\n", RunFile(main));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    [InlineData("discontiguous")]
    public void AGrammarRuleIndicatorIsAcceptedByPredicateDeclarations(string declaration)
    {
        var path = Write(
            $"{declaration}.pl",
            $$"""
            :- {{declaration}} token//1.

            token(X) --> [X].

            :- initialization((phrase(token(a), [a]), write(ok), nl)).
            """
        );

        Assert.Equal("ok\n", RunFile(path));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    [InlineData("discontiguous")]
    public void AMalformedGrammarRuleIndicatorIsReportedByPredicateDeclarations(string declaration)
    {
        var path = Write($"{declaration}-bad.pl", $":- {declaration} token//-1.\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.False(loaded.Success);
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

        var main = Write(
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
        var path = Write(
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
        var path = Write("missing.pl", ":- use_module(nowhere).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.ModuleNotFound);
    }

    [Fact]
    public void AMalformedExportListIsReported()
    {
        var path = Write("bad.pl", ":- module(bad, [oops]).\n");

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultFile(path);

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidModuleDeclaration);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Extended)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void IsoInterfacesAndBodiesPrepareImportsBeforeExecution(PrologLanguageMode mode)
    {
        const string source = """
            :- module(values).
            :- export(value/1).
            :- end_module(values).

            :- module(client).
            :- export(run/0).
            :- end_module(client).

            :- body(values).
            value(ok).
            :- end_body(values).

            :- body(client).
            :- import(values, value/1).
            run :- value(X), write(X).
            :- initialization(run).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(mode) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-modules.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Fact]
    public void IsoModuleMarkersMustBePairedAndOrdered()
    {
        const string source = """
            :- body(missing).
            p.
            :- end_body(other).
            """;

        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(source, "bad-iso-modules.pl");

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidIsoModuleText);
    }

    [Fact]
    public void AnIsoBodyRequiresItsPreviouslyLoadedInterface()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = TextWriter.Null };

        LoadResult loaded = engine.ConsultText(
            """
            :- body(missing).
            p.
            :- end_body(missing).
            """,
            "missing-interface.pl"
        );

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidIsoModuleText);
    }

    [Fact]
    public void AnInterfaceAndItsBodyMayBePreparedFromSeparateModuleTexts()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult interfaceLoaded = engine.ConsultText(
            """
            :- module(separate).
            :- export(value/1).
            :- end_module(separate).
            """,
            "separate-interface.pl"
        );
        LoadResult bodyLoaded = engine.ConsultText(
            """
            :- body(separate).
            value(ok).
            :- initialization((value(X), write(X))).
            :- end_body(separate).
            """,
            "separate-body.pl"
        );

        Assert.Empty(interfaceLoaded.Diagnostics);
        Assert.Empty(bodyLoaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Fact]
    public void AModuleBodyCanBeEmbeddedInTheUserBodyWithoutLeakingReaderState()
    {
        const string source = """
            :- module(nested).
            :- export(value/1).
            :- op(500, xfx, likes).
            :- end_module(nested).

            :- body(user).
            before(user).
            :- body(nested).
            value(alice likes bob).
            :- end_body(nested).
            after(user).
            :- initialization((before(B), after(A), nested:value(V), write(B-A-V))).
            :- end_body(user).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "nested-user-body.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("user-user-likes(alice,bob)", output.ToString());
    }

    [Fact]
    public void UnbracketedModuleTextBelongsToUserAndInterfacesMayFollowOtherBodies()
    {
        const string source = """
            before(user).

            :- module(first).
            :- export(one/0).
            :- end_module(first).
            :- body(first).
            one.
            :- end_body(first).

            :- module(second).
            :- export(two/0).
            :- end_module(second).
            :- body(second).
            two.
            :- end_body(second).

            after(user).
            :- initialization((before(B), after(A), first:one, second:two, write(B-A))).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "mixed-module-text.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("user-user", output.ToString());
    }

    [Fact]
    public void MetapredicateDeclarationsExportTheirDefinedProcedures()
    {
        const string source = """
            :- module(runner).
            :- metapredicate(run(:)).
            :- end_module(runner).

            :- module(client).
            :- export(go/0).
            :- end_module(client).

            :- body(runner).
            run(Goal) :- call(Goal).
            :- end_body(runner).

            :- body(client).
            :- import(runner, run/1).
            local :- write(ok).
            go :- run(local).
            :- initialization(go).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "metapredicate-export.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Theory]
    [InlineData("true/0")]
    [InlineData("!/0")]
    public void AnIsoInterfaceCannotExportAPredefinedProcedure(string indicator)
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(
            $"""
            :- module(invalid).
            :- export({indicator}).
            :- end_module(invalid).
            """,
            "invalid-export.pl"
        );

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidIsoModuleText);
    }

    [Theory]
    [InlineData(":- op(1300, xfx, bad).")]
    [InlineData(":- char_conversion(ab, c).")]
    [InlineData(":- set_prolog_flag(bounded, false).")]
    [InlineData(":- metapredicate(once(:)).")]
    public void AnIsoInterfaceRejectsInvalidDeclarations(string declaration)
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(
            $"""
            :- module(invalid).
            {declaration}
            :- end_module(invalid).
            """,
            "invalid-interface.pl"
        );

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidIsoModuleText);
    }

    [Fact]
    public void AnIsoBodyRejectsQualifiedAndPredefinedClauseHeads()
    {
        const string source = """
            :- module(invalid).
            :- end_module(invalid).
            :- body(invalid).
            invalid:p.
            true.
            :- end_body(invalid).
            """;

        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(source, "invalid-body-heads.pl");

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.InvalidIsoModuleText);
    }

    [Fact]
    public void IsoModuleReflectionUsesTheCallingModulesVisibleDatabase()
    {
        const string source = """
            :- module(values).
            :- export(item/1).
            :- end_module(values).

            :- module(client).
            :- export(run/0).
            :- end_module(client).

            :- body(values).
            :- dynamic(item/1).
            item(one).
            hidden(local).
            :- end_body(values).

            :- body(client).
            :- import(values, item/1).
            run :-
                findall(P, predicate_property(item(_), P), Properties),
                write(Properties), nl,
                findall(PI, current_predicate(PI), Predicates),
                write(Predicates), nl,
                findall(M, current_module(M), Modules),
                write(Modules).
            :- initialization(run).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-reflection.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal(
            "[dynamic,public,exported,imported_from(values),defined_in(values)]\n[run/0,item/1]\n[user,values,client]",
            output.ToString()
        );
    }

    [Fact]
    public void CurrentModuleRequiresAnAtomWhenBound() =>
        Assert.Equal(
            "type_error(atom,42)",
            PrologTestHost.RunGoal("catch(current_module(42), error(E, _), write(E))")
        );

    [Fact]
    public void IsoInterfacesGiveEachModuleItsOwnReaderAndFlagState()
    {
        const string source = """
            :- module(words).
            :- export(show/0).
            :- op(500, xfx, likes).
            :- set_prolog_flag(double_quotes, chars).
            :- end_module(words).

            :- module(codes).
            :- export(show_codes/0).
            :- end_module(codes).

            :- body(words).
            relation(alice likes bob).
            text("ab").
            show :-
                relation(R), text(T),
                current_op(500, xfx, likes),
                current_prolog_flag(double_quotes, Q),
                read_term(Input, []),
                write(R-T-Q-Input), nl.
            :- initialization(show).
            :- end_body(words).

            :- body(codes).
            text_codes("ab").
            show_codes :-
                text_codes(T),
                ( current_op(_, _, likes) -> O = leaked ; O = no_likes ),
                current_prolog_flag(double_quotes, Q),
                words:current_prolog_flag(double_quotes, WQ),
                write(T-O-Q-WQ).
            :- initialization(show_codes).
            :- end_body(codes).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso)
        {
            Output = output,
            Input = new StringReader("alice likes bob."),
        };

        LoadResult loaded = engine.ConsultText(source, "iso-reader-state.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("alice likes bob-[a,b]-chars-(alice likes bob)\n[97,98]-no_likes-codes-chars", output.ToString());
        Assert.False(engine.Program.Operators.IsOperator("likes"));
        Assert.Equal(DoubleQuotesMode.Codes, engine.Program.Flags.DoubleQuotes);
    }

    [Fact]
    public void QualifyingWithANonexistentModuleRaisesThePartTwoExistenceError() =>
        Assert.Equal(
            "existence_error(module,missing)",
            PrologTestHost.RunGoal("catch(missing:true, error(E, _), write_canonical(E))")
        );

    [Fact]
    public void IsoDatabaseOperationsUseTheirCallingModuleAndRejectImplicitImports()
    {
        const string source = """
            :- module(store).
            :- export(item/1).
            :- export(put/1).
            :- export(all/1).
            :- end_module(store).

            :- module(client).
            :- export(run/0).
            :- end_module(client).

            :- body(store).
            :- dynamic(item/1).
            put(X) :- assertz(item(X)).
            all(L) :- findall(X, item(X), L).
            :- end_body(store).

            :- body(client).
            :- import(store).
            run :-
                put(one),
                store:assertz(item(two)),
                all(L), write(L), nl,
                catch(assertz(item(three)), error(permission_error(modify, implicit, PI), _), write(PI)), nl,
                catch(clause(item(_), _), error(permission_error(access, implicit, PI2), _), write(PI2)).
            :- initialization(run).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-database.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("[one,two]\nitem/1\nitem/1", output.ToString());
    }

    [Fact]
    public void ClauseInspectsStaticProceduresInTheirDefiningModule()
    {
        const string source = """
            :- module(facts).
            :- export(item/1).
            :- end_module(facts).

            :- module(client).
            :- export(run/0).
            :- end_module(client).

            :- body(facts).
            item(one).
            item(X) :- X = two.
            hidden(secret).
            :- end_body(facts).

            :- body(client).
            :- import(facts, item/1).
            run :-
                findall(X, (clause(facts:item(X), B), call(B)), Clauses), write(Clauses), nl,
                facts:clause(hidden(X), B2), write(X-B2), nl,
                catch(clause(item(_), _), error(permission_error(access, implicit, PI), _), write(PI)).
            :- initialization(run).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-static-clause.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("[one,two]\nsecret-true\nitem/1", output.ToString());
    }

    [Fact]
    public void DynamicModuleClausesRetainTheirUnqualifiedSourceBodies()
    {
        const string source = """
            :- module(store).
            :- export(item/1).
            :- end_module(store).

            :- body(store).
            :- dynamic(item/1).
            local(one).
            item(X) :- local(X).
            :- initialization((
                clause(item(X), B), B = local(X), call(B), write(X), nl,
                retract((item(Y) :- local(Y))),
                ( item(_) -> write(present) ; write(removed) )
            )).
            :- end_body(store).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-dynamic-clause.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("one\nremoved", output.ToString());
    }

    [Fact]
    public void IsoMetapredicateArgumentsUseStaticAndExplicitCallingContexts()
    {
        const string source = """
            :- module(runner).
            :- export(run/1).
            :- metapredicate(run(:)).
            :- end_module(runner).

            :- module(client).
            :- export(go/0).
            :- end_module(client).

            :- body(runner).
            run(Goal) :- call(Goal).
            local :- write(runner).
            :- end_body(runner).

            :- body(client).
            :- import(runner, run/1).
            local :- write(client).
            go :-
                run(local),
                runner:run(local),
                predicate_property(run(_), metapredicate(Template)),
                write(Template).
            :- initialization(go).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-meta.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("clientrunnerrun(:)", output.ToString());
    }

    [Fact]
    public void ExplicitQualificationSetsTheContextOfBuiltInMetaPredicatesAndControls()
    {
        const string source = """
            :- module(first).
            :- export(run/0).
            :- end_module(first).

            :- module(second).
            :- export(local/1).
            :- end_module(second).

            :- body(first).
            local(first).
            run :-
                second:findall(X, local(X), L), write(L),
                second:(local(Y), once(local(Y))), write(Y),
                second:(\+ local(missing)).
            :- initialization(run).
            :- end_body(first).

            :- body(second).
            local(second).
            :- end_body(second).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-explicit-context.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("[second]second", output.ToString());
    }

    [Fact]
    public void AnUnknownProcedureInAnIsoBodyCarriesItsModuleInTheError()
    {
        const string source = """
            :- module(contextual).
            :- export(run/0).
            :- end_module(contextual).

            :- body(contextual).
            run :- catch(missing, error(existence_error(procedure, contextual:missing/0), _), write(ok)).
            :- initialization(run).
            :- end_body(contextual).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-unknown.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Theory]
    [InlineData(":- module(compatibility, []).")]
    [InlineData(":- use_module(compatibility).")]
    [InlineData(":- meta_predicate(run(0)).")]
    public void StrictIsoRejectsCompatibilityModuleDirectives(string source)
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = TextWriter.Null };

        LoadResult loaded = engine.ConsultText(source, "compatibility.pl");

        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == CompilerDiagnosticIds.StrictIsoViolation);
    }

    [Fact]
    public void IsoReexportsPreserveTheImportingAndDefiningModules()
    {
        const string source = """
            :- module(base).
            :- export(value/1).
            :- end_module(base).

            :- module(facade).
            :- reexport(base).
            :- end_module(facade).

            :- module(client).
            :- export(run/0).
            :- end_module(client).

            :- body(base).
            value(ok).
            :- end_body(base).

            :- body(facade).
            :- end_body(facade).

            :- body(client).
            :- import(facade).
            run :-
                value(X), write(X),
                predicate_property(value(_), imported_from(facade)),
                predicate_property(value(_), defined_in(base)),
                current_predicate(facade:value/1),
                write(done).
            :- initialization(run).
            :- end_body(client).
            """;

        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultText(source, "iso-reexport.pl");

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("okdone", output.ToString());
    }

    [Fact]
    public void CompilerResolutionUsesModuleMetadataInstalledByGeneratedCode()
    {
        var catalog = new ModuleCatalog();
        var runtimeIndicator = new ModulePredicateIndicator("apply", 1);
        ModulePredicateDefinition generated = catalog.Declare("generated").Predicate(runtimeIndicator);
        generated.Exported = true;
        generated.Defined = true;
        generated.MetapredicateTemplate = ":";
        Assert.True(catalog.Declare("runtime").TryImport(runtimeIndicator, "generated", out _));

        var modules = new ModuleTable(catalog);
        var compilerIndicator = new PredicateIndicator("apply", 1);

        Assert.True(modules.Exports("generated", compilerIndicator));
        Assert.Contains(compilerIndicator, modules.ExportsOf("generated"));
        Assert.Equal("generated", modules.ImportedFrom("runtime", compilerIndicator));
        Assert.Equal([0], Assert.IsType<int[]>(modules.MetaArgumentsOf("runtime", compilerIndicator)));
    }
}
