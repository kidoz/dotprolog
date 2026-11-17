using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DotProlog.CodeGen.CSharp.Tests;

/// <summary>
/// Generates a facade, compiles it with Roslyn, loads it, and calls it.
/// </summary>
/// <remarks>
/// Asserting on generated text only proves the text. Compiling and running it is what proves the
/// generator emits C# that works — including that the marshalling expressions type-check.
/// </remarks>
public sealed class GeneratedFacadeTests
{
    [Fact]
    public void GeneratedEnumerationRejectsNestedCallsAndPreservesRemainingAnswers()
    {
        var contract = ContractReader.Read(
            ":- clr_module('Nested'). :- clr_export(values/1, nondet, [out(value, atom)]). :- clr_export(check/0, semidet, []).",
            "Generated.Nested"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, "values(first). values(second). check.", "values.pl");
        var type = CompileGenerated(source).GetType("Generated.Nested.NestedModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        using var enumerator = ((IEnumerable<string>)Call(module, type, "Values", CancellationToken.None)!).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        var error = Assert.Throws<TargetInvocationException>(() => Call(module, type, "Check"));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.True(enumerator.MoveNext());
        Assert.Equal("second", enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.True((bool)Call(module, type, "Check")!);
    }

    [Fact]
    public void GeneratedForeignRegistryServesDirectCallsTailCallsAndRuntimeConsultation()
    {
        const string program = """
            :- initialization(main).
            main :- foreign_value(21, X), X == 42, tail(7, 14).
            tail(X, Y) :- foreign_value(X, Y).
            test_foreign :- foreign_value(3, 6), tail(4, 8).
            """;
        const string implementation = """

            internal static class ForeignPredicates
            {
                [global::DotProlog.Runtime.PrologPredicate(Name = "foreign_value", Arity = 2)]
                internal static bool Double(ref global::DotProlog.Runtime.PrologContext context,
                    global::DotProlog.Runtime.Cell input, global::DotProlog.Runtime.Cell output)
                    => context.Machine.Unify(output, global::DotProlog.Runtime.Cell.Integer60(input.Integer * 2));
            }
            """;

        var source = EntryPointGenerator.Generate("Generated.Foreign", [("foreign.pl", program)]) + implementation;
        Assert.Equal(0, RunGeneratedApplication(source, "Generated.Foreign"));

        source = EntryPointGenerator.GenerateTestHost("Generated.ForeignTests", [("foreign.pl", program)]) + implementation;
        var type = CompileGenerated(source).GetType("Generated.ForeignTests.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);
        Assert.True(engine.Query("test_foreign").Prove());
        engine.ConsultOrThrow("runtime_foreign :- foreign_value(5, 10), tail(6, 12).\n", "runtime.pl");
        Assert.True(engine.Query("runtime_foreign").Prove());
    }

    [Fact]
    public void GeneratedCutCleanupSharesVariablesAcrossExecutionBoundaries()
    {
        var source = EntryPointGenerator.GenerateTestHost(
            "Generated.CleanupVariables",
            [
                (
                    "cleanup.pl",
                    """
                    p(a). p(b).
                    commit(Y) :- call_cleanup(p(_), Y = done), !.
                    runtime_commit(Y) :- call_cleanup(runtime_choice(_), Y = done), !.
                    test_cleanup :- commit(Y), Y == done, runtime_commit(Z), Z == done,
                        freeze(A, B = woke), call_cleanup(p(_), A = done), !, B == woke.
                    """
                ),
            ]
        );
        var type = CompileGenerated(source).GetType("Generated.CleanupVariables.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);
        engine.ConsultOrThrow("runtime_choice(a). runtime_choice(b).\n", "runtime.pl");
        Assert.True(engine.Query("test_cleanup").Prove());
        Assert.True(engine.Query("call_cleanup(p(_), Y = done), !, Y == done").Prove());
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void GeneratedIndexesRelocateBucketsAndPreserveOrderAndFinalCleanup(Runtime.PrologLanguageMode mode)
    {
        var facts = string.Join('\n', Enumerable.Range(0, 80).Select(i => $"p({i}, {i}). only({i})."));
        var program =
            "p(_, first).\n"
            + facts
            + "\np(79, duplicate). p(_, last).\n"
            + """
                q(f(a), first). q(f(b), rejected). q(f(a), last).
                test_index :-
                    findall(V, p(79,V), [first,79,duplicate,last]),
                    findall(V, p(missing,V), [first,last]),
                    findall(V, q(f(a),V), [first,last]),
                    only(79),
                    \+ only(missing).
                """
            + "\n";
        var source = EntryPointGenerator.GenerateTestHost("Generated.Indexes", [("indexes.pl", program)], mode);
        Assert.Contains("execution.EnterStatic", source, StringComparison.Ordinal);
        var type = CompileGenerated(source).GetType("Generated.Indexes.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);
        Assert.True(engine.Query("test_index").Prove());
        Assert.Equal(83, engine.Query("p(_,V)").Solutions().Count());
        if (mode == Runtime.PrologLanguageMode.Modern)
            Assert.True(engine.Query("setup_call_cleanup(true, only(79), nb_setval(done,yes)), nb_getval(done,yes)").Prove());
        var host = new Runtime.PrologHost(engine.Machine);
        Assert.True(host.Prove(host.Bind("only", 1), Runtime.PrologInput.Integer(79)));
        Assert.False(engine.Machine.HasAlternatives);
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void GeneratedUserDefinitionsReplaceNativeCallsAcrossUnitsAndExecutionPaths(Runtime.PrologLanguageMode mode)
    {
        const string definitions = """
            msort(_, generated_sort).
            between(0, 0, zero).
            between(1, 1, one).
            """;
        const string calls = """
            direct(Sort, Value) :- msort([], Sort), between(1, 1, Value).
            meta(Sort, Value) :- call(msort([], Sort)), call(between(0, 0, Value)).
            """;
        var source = EntryPointGenerator.GenerateTestHost(
            "Generated.NativeShadowing",
            [("definitions.pl", definitions + "\n"), ("calls.pl", calls + "\n")],
            mode
        );
        var type = CompileGenerated(source).GetType("Generated.NativeShadowing.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);

        Assert.True(engine.Query("direct(generated_sort, one), meta(generated_sort, zero)").Prove());
        Assert.True(
            engine
                .ConsultText(
                    "bytecode(Sort, Value) :- msort([], Sort), between(1, 1, Value).\n"
                        + "bytecode_meta(Sort, Value) :- call(msort([], Sort)), call(between(0, 0, Value)).\n"
                )
                .Success
        );
        Assert.True(engine.Query("bytecode(generated_sort, one), bytecode_meta(generated_sort, zero)").Prove());
    }

    [Fact]
    public void GeneratedNativeCallsObserveLaterConsultedDefinitions()
    {
        const string calls = """
            direct(Sort, Value) :- msort([], Sort), between(1, 1, Value).
            meta(Sort, Value) :- call(msort([], Sort)), call(between(0, 0, Value)).
            """;
        var source = EntryPointGenerator.GenerateTestHost("Generated.LateNativeShadowing", [("calls.pl", calls)]);
        var type = CompileGenerated(source).GetType("Generated.LateNativeShadowing.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);
        Assert.True(engine.ConsultText("msort(_, consulted_sort). between(0, 0, zero). between(1, 1, one).").Success);

        Assert.True(engine.Query("direct(consulted_sort, one), meta(consulted_sort, zero)").Prove());
    }

    [Theory]
    [InlineData(":- initialization(loop). loop :- loop. test_ok.")]
    [InlineData("loop :- loop. :- loop. test_ok.")]
    public void GeneratedFactoryInitializationIsTimedAndItsWorkerStops(string program)
    {
        var source = EntryPointGenerator.GenerateTestHost("Generated.Tests", [("tests.pl", program)]);
        var type = CompileGenerated(source).GetType("Generated.Tests.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        var runner = new Testing.PrologTestRunner(factory) { Timeout = TimeSpan.FromMilliseconds(250) };
        var discovery = Assert.Throws<TimeoutException>(() => runner.Discover());
        Assert.Contains("worker stopped after cancellation", discovery.Message, StringComparison.Ordinal);
        var result = runner.Run(new Testing.PrologTest("test_ok", 0));
        Assert.False(result.Succeeded);
        Assert.Contains("worker stopped after cancellation", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepSourceReturnsPositionedDiagnostics(bool testHost)
    {
        var input = new string('(', 20_000) + "a" + new string(')', 20_000) + ".";
        IReadOnlyList<Syntax.Diagnostic> diagnostics;
        var generated = testHost
            ? EntryPointGenerator.GenerateTestHost(
                "Generated.Deep",
                [("deep.pl", input)],
                Runtime.PrologLanguageMode.Modern,
                Runtime.PrologFlagOverrides.None,
                out diagnostics
            )
            : EntryPointGenerator.Generate(
                "Generated.Deep",
                [("deep.pl", input)],
                Runtime.PrologLanguageMode.Modern,
                Runtime.PrologFlagOverrides.None,
                out diagnostics
            );
        Assert.Empty(generated);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DPL0012", diagnostic.Id);
        Assert.Equal("deep.pl", diagnostic.FileName);
        Assert.Equal(1, diagnostic.Span.Line);
    }

    [Fact]
    public void GeneratedBlocksObserveCancellation()
    {
        var contract = ContractReader.Read(":- clr_module('Loop'). :- clr_export(loop/0, semidet, []).", "Generated.Loop");
        var source = FacadeGenerator.Generate(contract.Contract!, "loop :- loop.", "loop.pl");
        var type = CompileGenerated(source).GetType("Generated.Loop.LoopModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        using var cancellation = new CancellationTokenSource();
        engine.Machine.CancellationToken = cancellation.Token;
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var thrown = Assert.Throws<TargetInvocationException>(() => Call(module, type, "Loop"));
        Assert.IsType<OperationCanceledException>(thrown.InnerException);
        Assert.False(engine.Machine.IsRunning);
    }

    [Fact]
    public void GeneratedStreamingCancellationAppliesBeforeTheFirstSolution()
    {
        var contract = ContractReader.Read(
            ":- clr_module('InitialCancel'). :- clr_export(loop/1, nondet, [out(value, atom)]).",
            "Generated.InitialCancel"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, "loop(X) :- loop(X).", "loop.pl");
        var type = CompileGenerated(source).GetType("Generated.InitialCancel.InitialCancelModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        var previous = engine.Machine.CancellationToken;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var values = (IEnumerable<string>)Call(module, type, "Loop", cancellation.Token)!;
        using var enumerator = values.GetEnumerator();

        Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
        Assert.False(engine.Machine.IsRunning);
        Assert.Equal(previous, engine.Machine.CancellationToken);
        Assert.True(engine.Query("true").Prove());
    }

    [Fact]
    public void GeneratedStreamingCancellationAppliesDuringRedoAndRestoresMachineState()
    {
        var contract = ContractReader.Read(
            ":- clr_module('RedoCancel'). :- clr_export(values/1, nondet, [out(value, atom)]).",
            "Generated.RedoCancel"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, "values(first). values(X) :- values(X).", "values.pl");
        var type = CompileGenerated(source).GetType("Generated.RedoCancel.RedoCancelModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        var previous = engine.Machine.CancellationToken;
        using var cancellation = new CancellationTokenSource();
        var values = (IEnumerable<string>)Call(module, type, "Values", cancellation.Token)!;
        using var enumerator = values.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("first", enumerator.Current);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
        Assert.False(engine.Machine.IsRunning);
        Assert.False(engine.Machine.HasAlternatives);
        Assert.Equal(previous, engine.Machine.CancellationToken);
        Assert.True(engine.Query("true").Prove());
    }

    [Fact]
    public void DisposingGeneratedEnumerationEarlyAllowsImmediateEngineReuse()
    {
        var contract = ContractReader.Read(
            ":- clr_module('EarlyDispose'). :- clr_export(values/1, nondet, [out(value, atom)]).",
            "Generated.EarlyDispose"
        );
        var source = FacadeGenerator.Generate(
            contract.Contract!,
            ":- dynamic(values/1). values(first). values(second).",
            "values.pl"
        );
        var type = CompileGenerated(source).GetType("Generated.EarlyDispose.EarlyDisposeModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        var values = (IEnumerable<string>)Call(module, type, "Values", CancellationToken.None)!;
        var enumerator = values.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("first", enumerator.Current);

        enumerator.Dispose();

        Assert.False(engine.Machine.HasAlternatives);
        Assert.Equal("first", values.First());
        Assert.True(engine.Query("retractall(values(_)), assertz(values(reused)), values(reused)").Prove());
    }

    [Fact]
    public void ParameterlessCreateOwnsAndDeterministicallyDisposesItsEngine()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-owned-facade-").FullName;
        try
        {
            var path = Path.Combine(directory, "owned.txt");
            var contract = ContractReader.Read(":- clr_module('Owned'). :- clr_export(ready/0, semidet, []).", "Generated.Owned");
            var source = FacadeGenerator.Generate(
                contract.Contract!,
                $":- initialization(open('{QuotedAtomText(path)}', write, _, [alias(owned)])). ready.",
                "owned.pl"
            );
            var type = CompileGenerated(source).GetType("Generated.Owned.OwnedModule")!;
            var module = (IDisposable)type.GetMethod("Create", Type.EmptyTypes)!.Invoke(null, null)!;
            Assert.Throws<IOException>(() => OpenExclusive(path));

            module.Dispose();
            module.Dispose();

            using var stream = OpenExclusive(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParameterlessCreateDisposesItsEngineWhenInitializationFails()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-failed-owned-facade-").FullName;
        try
        {
            var path = Path.Combine(directory, "owned.txt");
            var contract = ContractReader.Read(
                ":- clr_module('FailedOwned'). :- clr_export(ready/0, semidet, []).",
                "Generated.FailedOwned"
            );
            var source = FacadeGenerator.Generate(
                contract.Contract!,
                $":- initialization((open('{QuotedAtomText(path)}', write, _, [alias(owned)]), fail)). ready.",
                "owned.pl"
            );
            var type = CompileGenerated(source).GetType("Generated.FailedOwned.FailedOwnedModule")!;

            var exception = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Create", Type.EmptyTypes)!.Invoke(null, null)
            );
            Assert.IsType<Runtime.PrologException>(exception.InnerException);
            using var stream = OpenExclusive(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CallerProvidedEngineRemainsCallerOwnedWhenFacadeIsDisposed()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-borrowed-facade-").FullName;
        try
        {
            var path = Path.Combine(directory, "borrowed.txt");
            var contract = ContractReader.Read(
                ":- clr_module('Borrowed'). :- clr_export(ready/0, semidet, []).",
                "Generated.Borrowed"
            );
            var source = FacadeGenerator.Generate(
                contract.Contract!,
                $":- initialization(open('{QuotedAtomText(path)}', write, _, [alias(borrowed)])). ready.",
                "borrowed.pl"
            );
            var type = CompileGenerated(source).GetType("Generated.Borrowed.BorrowedModule")!;
            using var engine = new Compiler.PrologEngine();
            var module = (IDisposable)type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

            module.Dispose();

            Assert.True(engine.Query("ready").Prove());
            Assert.Throws<IOException>(() => OpenExclusive(path));
            engine.Dispose();
            using var stream = OpenExclusive(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void IntegerFacadeAcceptsTheFullClrInt64Range(long expected)
    {
        var contract = ContractReader.Read(
            ":- clr_module('Int64'). :- clr_export(identity/2, det, [in(value, integer), out(result, integer)]).",
            "Generated.Int64"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, "identity(X, X).", "identity.pl");
        var type = CompileGenerated(source).GetType("Generated.Int64.Int64Module")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        Assert.Equal(expected, Call(module, type, "Identity", expected));
    }

    [Fact]
    public void MissingContractedPredicateIsReportedDuringGeneration()
    {
        var contract = ContractReader.Read(
            ":- clr_module('Missing'). "
                + ":- clr_export(present/0, semidet, []). "
                + ":- clr_export(absent/1, det, [out(value, atom)]).",
            "Generated.Missing"
        );

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            "present.",
            "missing.pl",
            Runtime.PrologLanguageMode.Modern,
            Runtime.PrologFlagOverrides.None,
            out var diagnostics
        );

        Assert.Empty(source);
        var diagnostic = Assert.Single(diagnostics, diagnostic => diagnostic.Id == CodeGenDiagnosticIds.MissingPredicate);
        Assert.Equal("missing.pl", diagnostic.FileName);
        Assert.Contains("absent/1", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialDynamicRationalClausesRoundTripThroughTheGeneratedFacade()
    {
        var contract = ContractReader.Read(
            ":- clr_module('DynamicRational'). :- clr_export(value/1, nondet, [out(value, term)]).",
            "Generated.DynamicRational"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, ":- dynamic(value/1). value(1r3). value(6r4).", "rational.pl");
        var type = CompileGenerated(source).GetType("Generated.DynamicRational.DynamicRationalModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        var values = (IEnumerable<Runtime.PrologValue>)Call(module, type, "Value", CancellationToken.None)!;
        Runtime.PrologRational[] rationals = [.. values.Select(value => Assert.IsType<Runtime.PrologRational>(value))];
        Assert.Equal(2, rationals.Length);
        Assert.Equal(1, (int)rationals[0].Numerator);
        Assert.Equal(3, (int)rationals[0].Denominator);
        Assert.Equal(3, (int)rationals[1].Numerator);
        Assert.Equal(2, (int)rationals[1].Denominator);
    }

    private static readonly string[] SplitInput = ["a", "b"];
    private static readonly Runtime.PrologFlagOverrides CodesOverride = new() { DoubleQuotes = Runtime.DoubleQuotesMode.Codes };

    private const string PrologSource = """
        discount(Price, Percent, Result) :- Result is Price - (Price * Percent / 100).

        colour(red).
        colour(green).
        colour(blue).

        in_stock(widget).
        in_stock(gadget).

        stock_level(widget, 7).

        runtime_bridge(X) :- runtime_value(X).

        token(X) --> [X].
        grammar_accepts(X) :- phrase(token(X), [X]).
        look_ahead(X), [X] --> [X].
        grammar_looks_ahead(X) :- phrase(look_ahead(X), [X, tail], [X, tail]).

        :- dynamic dynamic_value/1.
        dynamic_value(first).

        prepared_before.
        check_preparation :-
            prepared_before,
            catch((prepared_after, fail), error(existence_error(procedure, _), _), true),
            write(prepared).
        :- check_preparation.
        prepared_after.

        split([], [], []).
        split([H|T], [H|L], R) :- split(T, L, R).
        split([H|T], L, [H|R]) :- split(T, L, R).

        pair(one). pair(two).
        match(one). match(two).
        chained(X) :- pair(X), match(X).
        note(_).
        audited(X) :- chained(X), note(X).

        strange_atom('tab\tand "quote" and\nnewline').

        catalogued(widget).
        catalogued(gadget).

        % A big-integer constant compiled into generated C#, exercised through
        % promotion, demotion, and identity in the compiled execution path.
        big_roundtrip(ok) :-
            X = 123456789012345678901234567890,
            Y is 123456789012345678900000000000 + 1234567890,
            X == Y,
            Z is X * X,
            Z =:= 15241578753238836750190519987501905210000000000000000000000 - 15241578753238836750190519987501905210000000000000000000000 + Z,
            W is X - X + 7,
            W == 7.
        """;

    private const string Contract = """
        :- clr_module('Pricing').
        :- clr_namespace('Generated.Pricing').

        :- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
        :- clr_export(colour/1, nondet, [out(colour, atom)]).
        :- clr_export(in_stock/1, semidet, [in(item, atom)]).
        :- clr_export(stock_level/2, semidet, [in(item, atom), out(level, integer)]).
        :- clr_export(runtime_bridge/1, nondet, [out(value, atom)]).
        :- clr_export(grammar_accepts/1, semidet, [in(value, atom)]).
        :- clr_export(grammar_looks_ahead/1, semidet, [in(value, atom)]).
        :- clr_export(dynamic_value/1, nondet, [out(value, atom)]).
        :- clr_export(split/3, nondet, [in(items, list(atom)), out(left, list(atom)), out(right, list(atom))]).
        :- clr_export(audited/1, nondet, [out(value, atom)]).
        :- clr_export(catalogued/1, nondet, [in(item, atom)]).
        :- clr_export(big_roundtrip/1, semidet, [in(value, atom)]).
        """;

    private static ModuleContract ReadContract()
    {
        var result = ContractReader.Read(Contract, "Fallback", "pricing.dpli");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.Contract!;
    }

    private static object CreateModule(out Type moduleType)
    {
        var source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");
        var assembly = CompileGenerated(source);
        moduleType = assembly.GetType("Generated.Pricing.PricingModule")!;
        Assert.NotNull(moduleType);

        return moduleType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;
    }

    [Theory]
    [InlineData(":- initialization(fail). :- initialization(halt(42)).", 1)]
    [InlineData(":- halt(7). :- initialization(halt(9)).", 7)]
    [InlineData(":- throw(problem).", 70)]
    public void GeneratedApplicationPropagatesStartupFailureHaltAndErrors(string program, int expected)
    {
        var source = EntryPointGenerator.Generate("Generated.Startup", [("startup.pl", program)]);

        Assert.Equal(expected, RunGeneratedApplication(source, "Generated.Startup"));
    }

    [Fact]
    public void GeneratedApplicationDrainsRuntimeConsultGoals()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-generated-consult-").FullName;
        try
        {
            var dependency = Path.Combine(directory, "dependency.pl");
            File.WriteAllText(dependency, ":- initialization(halt(11)).");
            var program = $":- initialization(consult('{QuotedAtomText(dependency)}')).";
            var source = EntryPointGenerator.Generate("Generated.RuntimeConsult", [(dependency + ".main", program)]);

            Assert.Equal(11, RunGeneratedApplication(source, "Generated.RuntimeConsult"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeneratedApplicationRunsMainAfterOtherUnitsInitialize()
    {
        var source = EntryPointGenerator.Generate(
            "Generated.GlobalMain",
            [
                ("first.pl", ":- initialization((initialized -> halt(0) ; halt(9)), main)."),
                ("second.pl", ":- initialization(assertz(initialized))."),
            ]
        );

        Assert.Equal(0, RunGeneratedApplication(source, "Generated.GlobalMain"));
    }

    [Fact]
    public void BuildTimeDependencyDirectivesAndInitializersRunAtFacadeStartup()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-generated-dependency-").FullName;
        try
        {
            var dependency = Path.Combine(directory, "dependency.pl");
            File.WriteAllText(dependency, ":- assertz(directive_ready).\n:- initialization(assertz(initialization_ready)).");
            var main = Path.Combine(directory, "main.pl");
            var contract = ContractReader.Read(
                ":- clr_module('Startup'). :- clr_export(ready/0, semidet, []).",
                "Generated.Dependency"
            );
            var source = FacadeGenerator.Generate(
                contract.Contract!,
                ":- ensure_loaded(dependency). ready :- directive_ready, initialization_ready.",
                main
            );
            var type = CompileGenerated(source).GetType("Generated.Dependency.StartupModule")!;
            using var engine = new Compiler.PrologEngine();

            var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

            Assert.Equal(true, Call(module, type, "Ready"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuildTimeUseModuleDefersDependencyStartupAndImportsItsReadyPredicate()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-generated-import-").FullName;
        try
        {
            var dependency = Path.Combine(directory, "dependency.pl");
            File.WriteAllText(
                dependency,
                ":- module(dependency, [ready/0]).\n"
                    + ":- assertz(directive_ready).\n"
                    + ":- initialization(assertz(initialization_ready)).\n"
                    + "ready :- directive_ready, initialization_ready."
            );
            var main = Path.Combine(directory, "main.pl");
            var contract = ContractReader.Read(
                ":- clr_module('ImportStartup'). :- clr_export(ready/0, semidet, []).",
                "Generated.ImportStartup"
            );
            var source = FacadeGenerator.Generate(contract.Contract!, ":- use_module(dependency).", main);
            var type = CompileGenerated(source).GetType("Generated.ImportStartup.ImportStartupModule")!;
            using var engine = new Compiler.PrologEngine();

            var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

            Assert.Equal(true, Call(module, type, "Ready"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuildTimeDependencyErrorsAreNotExecutedByTheGenerator()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-generated-error-").FullName;
        try
        {
            var dependency = Path.Combine(directory, "dependency.pl");
            File.WriteAllText(dependency, ":- throw(deferred_dependency_error).");
            var main = Path.Combine(directory, "main.pl");
            var source = EntryPointGenerator.Generate("Generated.DependencyError", [(main, ":- ensure_loaded(dependency).")]);

            Assert.Equal(70, RunGeneratedApplication(source, "Generated.DependencyError"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeneratedFacadeDrainsRuntimeConsultInitialization()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-facade-consult-").FullName;
        try
        {
            var dependency = Path.Combine(directory, "runtime.pl");
            File.WriteAllText(dependency, ":- initialization(assertz(runtime_ready)).");
            var contract = ContractReader.Read(
                ":- clr_module('RuntimeConsult'). :- clr_export(ready/0, semidet, []).",
                "Generated.RuntimeFacade"
            );
            var source = FacadeGenerator.Generate(
                contract.Contract!,
                $":- initialization(consult('{QuotedAtomText(dependency)}')). ready :- runtime_ready.",
                "facade.pl"
            );
            var type = CompileGenerated(source).GetType("Generated.RuntimeFacade.RuntimeConsultModule")!;
            using var engine = new Compiler.PrologEngine();

            var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

            Assert.Equal(true, Call(module, type, "Ready"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(":- initialization(fail).")]
    [InlineData(":- initialization(halt(6)).")]
    public void GeneratedFacadeRejectsUnsuccessfulInitialization(string program)
    {
        var contract = ContractReader.Read(
            ":- clr_module('FailedStartup'). :- clr_export(ready/0, semidet, []).",
            "Generated.FailedStartup"
        );
        var source = FacadeGenerator.Generate(contract.Contract!, program + " ready.", "startup.pl");
        var type = CompileGenerated(source).GetType("Generated.FailedStartup.FailedStartupModule")!;
        using var engine = new Compiler.PrologEngine();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])
        );
        Assert.IsType<Runtime.PrologException>(exception.InnerException);
        Assert.True(engine.Query("true").Prove());
    }

    [Fact]
    public void GeneratedDeclarationOnlyProceduresAreDefinedEmptyAndStatic()
    {
        var source = EntryPointGenerator.GenerateTestHost(
            "Generated.Declarations",
            [
                (
                    "declarations.pl",
                    ":- multifile(absent/1). :- discontiguous(scattered/0). "
                        + "test_declarations :- \\+ absent(_), \\+ scattered, current_predicate(absent/1), "
                        + "current_predicate(scattered/0), "
                        + "catch(assertz(scattered), error(permission_error(modify,static_procedure,scattered/0), _), true)."
                ),
            ]
        );
        var type = CompileGenerated(source).GetType("Generated.Declarations.PrologProgram")!;
        var factory = type.GetMethod("CreateEngine", BindingFlags.NonPublic | BindingFlags.Static, [typeof(CancellationToken)])!
            .CreateDelegate<Func<CancellationToken, Compiler.PrologEngine>>();
        using var engine = factory(CancellationToken.None);

        Assert.True(engine.Query("test_declarations").Prove());
    }

    private static int RunGeneratedApplication(string source, string @namespace)
    {
        var type = CompileGenerated(source).GetType($"{@namespace}.PrologProgram")!;
        return (int)type.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
    }

    private static string QuotedAtomText(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static FileStream OpenExclusive(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private static Assembly CompileGenerated(string source)
    {
        var compilation = CSharpCompilation.Create(
            $"GeneratedFacade_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Foreign.SourceGenerator.PrologForeignPredicateGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest)
        );
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updated,
            out _,
            TestContext.Current.CancellationToken
        );
        compilation = (CSharpCompilation)updated;

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);

        Assert.True(
            emitted.Success,
            "The generated facade did not compile:\n"
                + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                + "\n\n"
                + source
        );

        return Assembly.Load(image.ToArray());
    }

    private static MetadataReference[] ReferenceAssemblies()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Private.CoreLib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Console.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.Numerics.dll")),
            MetadataReference.CreateFromFile(typeof(Testing.PrologTestHost).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Runtime.Machine).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Compiler.PrologEngine).Assembly.Location),
        ];
    }

    private static object? Call(object module, Type type, string method, params object?[] arguments) =>
        type.GetMethod(method)!.Invoke(module, arguments);

    [Fact]
    public void GeneratedFacadeCompilesAndLoads()
    {
        var module = CreateModule(out var type);

        Assert.NotNull(module);
        Assert.Contains(type.GetInterfaces(), i => i.Name == "IPricingModule");
    }

    [Fact]
    public void GeneratedFacadeContainsCompiledBlocksInsteadOfEmbeddedSource()
    {
        var source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        Assert.Contains("RegisterCompiledBlock", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsultOrThrow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("discount(Price, Percent, Result)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicExportReturnsItsOutput()
    {
        var module = CreateModule(out var type);

        Assert.Equal(90.0, Call(module, type, "Discount", 100.0, 10L));
    }

    [Fact]
    public void SemiDeterministicExportWithNoOutputsReturnsBool()
    {
        var module = CreateModule(out var type);

        Assert.Equal(true, Call(module, type, "InStock", "widget"));
        Assert.Equal(false, Call(module, type, "InStock", "sprocket"));
    }

    [Fact]
    public void BigIntegerConstantsSurviveTheGeneratedPath()
    {
        var module = CreateModule(out var type);

        Assert.Equal(true, Call(module, type, "BigRoundtrip", "ok"));
    }

    [Fact]
    public void SemiDeterministicExportWithAnOutputBecomesATryMethod()
    {
        var module = CreateModule(out var type);
        var tryStockLevel = type.GetMethod("TryStockLevel")!;

        object?[] found = ["widget", null];
        Assert.Equal(true, tryStockLevel.Invoke(module, found));
        Assert.Equal(7L, found[1]);

        object?[] missing = ["gadget", null];
        Assert.Equal(false, tryStockLevel.Invoke(module, missing));
    }

    [Fact]
    public void NondeterministicExportStreamsEverySolution()
    {
        var module = CreateModule(out var type);

        // Streaming methods take a CancellationToken, which reflection must supply explicitly.
        var colours = (IEnumerable<string>)Call(module, type, "Colour", CancellationToken.None)!;

        Assert.Equal(["red", "green", "blue"], colours);
    }

    [Fact]
    public void NondeterministicExportSurvivesBacktrackingThroughAnLcoReturn()
    {
        // chained/1 reaches match/1 by last-call optimisation while pair/1's choice point still
        // references its frame, and note/1 allocates a frame before backtracking. The compiled
        // blocks share the machine's stack-protection watermark, so both solutions must survive.
        var module = CreateModule(out var type);

        var values = (IEnumerable<string>)Call(module, type, "Audited", CancellationToken.None)!;

        Assert.Equal(["one", "two"], values);
    }

    [Fact]
    public void NondeterministicExportWithNoOutputsStreamsAUnitPerSolution()
    {
        var module = CreateModule(out var type);

        var found = (IEnumerable<Runtime.Unit>)Call(module, type, "Catalogued", "widget", CancellationToken.None)!;
        Assert.Single(found);

        var missing = (IEnumerable<Runtime.Unit>)Call(module, type, "Catalogued", "sprocket", CancellationToken.None)!;
        Assert.Empty(missing);
    }

    [Fact]
    public void ListsCrossTheBoundaryInBothDirections()
    {
        var module = CreateModule(out var type);

        var splits = (System.Collections.IEnumerable)Call(module, type, "Split", SplitInput, CancellationToken.None)!;
        List<string> shapes = [];

        foreach (var split in splits)
        {
            var resultType = split.GetType();
            var left = (IReadOnlyList<string>)resultType.GetProperty("Left")!.GetValue(split)!;
            var right = (IReadOnlyList<string>)resultType.GetProperty("Right")!.GetValue(split)!;
            shapes.Add($"{string.Concat(left)}|{string.Concat(right)}");
        }

        Assert.Equal(["ab|", "a|b", "b|a", "|ab"], shapes);
    }

    [Fact]
    public void SeveralOutputsBecomeAGeneratedRecord()
    {
        var source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        Assert.Contains("public readonly record struct SplitResult(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledPredicateCallsAConsultedPredicate()
    {
        var unused = CreateModule(out var type);
        _ = unused;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow("runtime_value(alpha). runtime_value(beta).", "runtime.pl");

        var values = (IEnumerable<string>)Call(module, type, "RuntimeBridge", CancellationToken.None)!;

        Assert.Equal(["alpha", "beta"], values);
    }

    [Fact]
    public void CompiledGrammarRulesCrossBothExecutionBoundaries()
    {
        var module = CreateModule(out var type);

        Assert.Equal(true, Call(module, type, "GrammarAccepts", "token"));
        Assert.Equal(true, Call(module, type, "GrammarLooksAhead", "token"));
    }

    [Fact]
    public void ConsultedPredicateCallsACompiledPredicate()
    {
        var unused = CreateModule(out var type);
        _ = unused;
        using var engine = new Compiler.PrologEngine();
        type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine]);
        engine.ConsultOrThrow("consulted_colour(X) :- colour(X).", "runtime.pl");
        var host = new Runtime.PrologHost(engine.Machine);
        var predicate = host.Bind("consulted_colour", 1);

        var values = host.CallAll(predicate, Runtime.PrologInput.Output)
            .Select(outputs => Runtime.PrologMarshal.ToAtom(outputs[0]))
            .ToArray();

        Assert.Equal(["red", "green", "blue"], values);
    }

    [Fact]
    public void ExecutableDirectiveRunsAgainstItsCompiledSourcePosition()
    {
        var unused = CreateModule(out var type);
        _ = unused;
        using var output = new StringWriter();
        using var engine = new Compiler.PrologEngine { Output = output };

        type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine]);

        Assert.Equal("prepared", output.ToString());
    }

    [Fact]
    public void CompiledDynamicPredicateKeepsItsLogicalDatabase()
    {
        var unused = CreateModule(out var type);
        _ = unused;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        var initial = (IEnumerable<string>)Call(module, type, "DynamicValue", CancellationToken.None)!;
        Assert.Equal(["first"], initial);

        Assert.True(engine.Query("assertz(dynamic_value(second))").Prove());
        var updated = (IEnumerable<string>)Call(module, type, "DynamicValue", CancellationToken.None)!;
        Assert.Equal(["first", "second"], updated);

        Assert.True(engine.Query("retract(dynamic_value(first))").Prove());
        var remaining = (IEnumerable<string>)Call(module, type, "DynamicValue", CancellationToken.None)!;
        Assert.Equal(["second"], remaining);
    }

    [Fact]
    public void APredicateNamedAfterACSharpKeywordStillGenerates()
    {
        // 'double' is a keyword: the parameter form needs an @ escape, the field form must not have
        // one, because '_@double2' is not a legal identifier.
        var contract = ContractReader.Read(
            ":- clr_module('Maths').\n:- clr_export(double/2, det, [in(double, integer), out(result, integer)]).",
            "Generated.Maths",
            "maths.dpli"
        );

        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(contract.Contract!, "double(X, Y) :- Y is X * 2.", "maths.pl");

        Assert.Contains("_double2;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_@", source, StringComparison.Ordinal);
        Assert.Contains("long @double", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrologNamesBecomeIdiomaticCSharpNames()
    {
        var source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        Assert.Contains("bool InStock(string item)", source, StringComparison.Ordinal);
        Assert.Contains("_host.Bind(\"in_stock\", 1)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void GeneratedCodeReadsIsoNegativeNumbersInBothModes(Runtime.PrologLanguageMode mode)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Numbers').
            :- clr_namespace('Generated.Numbers').
            :- clr_export(answer/1, semidet, [in(value, integer)]).
            """,
            "Generated.Numbers",
            "numbers.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(contract.Contract!, "answer(- 1).\n", "numbers.pl", mode);
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Numbers.NumbersModule")!;
        using var engine = new Compiler.PrologEngine(mode);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        Assert.Equal(true, Call(module, type, "Answer", -1L));
        Assert.Equal(false, Call(module, type, "Answer", 1L));
        Assert.True(engine.ConsultText("bytecode_answer(X) :- answer(X).\n").Success);
        Assert.Single(engine.Query("bytecode_answer(-1)").Solutions());
    }

    [Fact]
    public void StrictFacadeCompilesRunsAndRequiresAStrictEngine()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Strict').
            :- clr_namespace('Generated.Strict').
            :- clr_export(answer/1, semidet, [in(value, integer)]).
            """,
            "Generated.Strict",
            "strict.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            "answer(42).\n",
            "strict.pl",
            Runtime.PrologLanguageMode.StrictIso
        );
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Strict.StrictModule")!;
        var module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal(true, Call(module, type, "Answer", 42L));

        var mismatch = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [new Compiler.PrologEngine()])
        );
        Assert.Contains("requires StrictIso language mode", mismatch.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictIsoReviewCasesRunThroughGeneratedAndConsultedCode(bool consultInvoker)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('IsoReview').
            :- clr_namespace('Generated.IsoReview').
            :- clr_export(run_iso_review_case/1, semidet, [in(name, atom)]).
            """,
            "Generated.IsoReview",
            "iso-review.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        var prolog = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "strict_iso_review.pl"));
        const string invoker = "iso_review_invoke(Goal) :- call(Goal).";
        if (consultInvoker)
        {
            Assert.Contains(invoker, prolog, StringComparison.Ordinal);
            prolog = prolog.Replace(invoker, "", StringComparison.Ordinal);
        }

        var source = FacadeGenerator.Generate(contract.Contract!, prolog, "iso-review.pl", Runtime.PrologLanguageMode.StrictIso);
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.IsoReview.IsoReviewModule")!;
        using var engine = new Compiler.PrologEngine(Runtime.PrologLanguageMode.StrictIso);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        var loaded = engine.ConsultText(
            "bytecode_review(Name) :- run_iso_review_case(Name).\n" + (consultInvoker ? invoker + "\n" : "")
        );
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        string[] names = [.. engine.Query("iso_review_case(Name, _)").Solutions().Select(s => s["Name"].ToString())];
        Assert.Equal(125, names.Length);
        foreach (var name in names)
        {
            Assert.True((bool)Call(module, type, "RunIsoReviewCase", name)!, name);
            Assert.Single(engine.Query($"bytecode_review({name})").Solutions());
        }
    }

    [Fact]
    public void StrictFacadeRejectsAnExtensionAtBuildTime()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Strict').
            :- clr_namespace('Generated.Strict').
            :- clr_export(answer/0, semidet, []).
            """,
            "Generated.Strict",
            "strict.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var exception = Assert.Throws<ArgumentException>(() =>
            FacadeGenerator.Generate(
                contract.Contract!,
                "answer :- member(a, [a]).\n",
                "strict.pl",
                Runtime.PrologLanguageMode.StrictIso
            )
        );

        Assert.Contains(Compiler.CompilerDiagnosticIds.StrictIsoViolation, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.StrictIso, "on", "z")]
    [InlineData(Runtime.PrologLanguageMode.Modern, "off", "q")]
    public void GeneratedReaderUsesTheModeConversionDefault(Runtime.PrologLanguageMode mode, string flag, string predicate)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Conversion').
            :- clr_namespace('Generated.Conversion').
            :- clr_export(check/0, semidet, []).
            """,
            "Generated.Conversion",
            "conversion.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        var source = FacadeGenerator.Generate(
            contract.Contract!,
            $"""
            :- char_conversion(q, z).
            q.
            check :- {predicate}, current_prolog_flag(char_conversion, {flag}).
            """ + "\n",
            "conversion.pl",
            mode
        );
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Conversion.ConversionModule")!;
        using var engine = new Compiler.PrologEngine(mode);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        Assert.Equal(true, Call(module, type, "Check"));
        Assert.Single(engine.Query("check").Solutions());
    }

    [Fact]
    public void GeneratedStrictFacadePreservesIsoModuleContextMetadata()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('IsoContext').
            :- clr_namespace('Generated.IsoContext').
            :- clr_export(answer/1, det, [out(value, atom)]).
            """,
            "Generated.IsoContext",
            "iso-context.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        const string prolog =
            """
                :- module(contextual).
                :- set_prolog_flag(double_quotes, atom).
                :- char_conversion(q, z).
                :- op(500, xfx, export).
                :- export([answer/1, q/0]).
                :- end_module(contextual).

                :- body(contextual).
                'q'.
                q.
                item("one").
                answer(Value) :- current_prolog_flag('double_quotes', atom), clause(item(Value), true),
                    z, predicate_property('q', exported), \+ predicate_property(z, exported).
                :- end_body(contextual).
                """ + "\n";

        var source = FacadeGenerator.Generate(contract.Contract!, prolog, "iso-context.pl", Runtime.PrologLanguageMode.StrictIso);
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.IsoContext.IsoContextModule")!;
        using var engine = new Compiler.PrologEngine(Runtime.PrologLanguageMode.StrictIso);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        foreach (
            var goal in new[]
            {
                "current_prolog_flag(double_quotes, atom)",
                "clause(item(one), true)",
                "z",
                "predicate_property('q', exported)",
                "\\+ predicate_property(z, exported)",
            }
        )
        {
            Assert.True(engine.Query($"contextual:({goal})").Prove(), goal);
        }

        Assert.Equal("one", Call(module, type, "Answer"));
    }

    [Fact]
    public void FacadeWithAFlagOverrideReadsAndRunsUnderTheOverride()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Quoted').
            :- clr_namespace('Generated.Quoted').
            :- clr_export(text/1, semidet, [in(value, atom)]).
            """,
            "Generated.Quoted",
            "quoted.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            "text(\"hello\").",
            "quoted.pl",
            Runtime.PrologLanguageMode.Modern,
            new Runtime.PrologFlagOverrides { DoubleQuotes = Runtime.DoubleQuotesMode.Atom }
        );

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Quoted.QuotedModule")!;
        var module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        // "hello" was read as one atom at build time, and Create() seeded the same override.
        Assert.Equal(true, Call(module, type, "Text", "hello"));

        var mismatch = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [new Compiler.PrologEngine()])
        );
        Assert.Contains("requires double_quotes to start at atom", mismatch.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallRestoresDoubleQuotesAfterDirectiveReplay()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Replay').
            :- clr_namespace('Generated.Replay').
            :- clr_export(chars_text/1, semidet, [in(text, list(atom))]).
            """,
            "Generated.Replay",
            "replay.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            """
            :- set_prolog_flag(double_quotes, chars).
            chars_text("ab").
            """,
            "replay.pl",
            Runtime.PrologLanguageMode.Modern,
            CodesOverride
        );

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Replay.ReplayModule")!;
        using var engine = new Compiler.PrologEngine(Runtime.PrologLanguageMode.Modern, CodesOverride);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        // The source was one load unit: replaying its directive at install time must not leak the
        // flag into the host engine, matching what consulting the same file would leave behind.
        Assert.Equal(Runtime.DoubleQuotesMode.Codes, engine.Program.Flags.DoubleQuotes);
        Assert.Equal(true, Call(module, type, "CharsText", (object)SplitInput));
    }

    [Fact]
    public void GeneratedCodeSplitsDoubleQuotedTextIntoCodePoints()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Emoji').
            :- clr_namespace('Generated.Emoji').
            :- clr_export(check/0, semidet, []).
            """,
            "Generated.Emoji",
            "emoji.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            """
            text("😀é").
            check :- text([E, _]), char_code(E, 128512), atom_length('😀', 1).
            """,
            "emoji.pl",
            Runtime.PrologLanguageMode.Modern
        );

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Emoji.EmojiModule")!;
        var module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal(true, Call(module, type, "Check"));
    }

    [Fact]
    public void FrozenGoalsWakeInGeneratedCodeAndAcrossTheBoundary()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Frozen').
            :- clr_namespace('Generated.Frozen').
            :- clr_export(check/0, semidet, []).
            :- clr_export(mixed/0, semidet, []).
            :- clr_export(cut_cleanup/0, semidet, []).
            """,
            "Generated.Frozen",
            "frozen.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            """
            bind(1).
            check :-
                freeze(X, Y = woke), X = go, Y == woke,
                freeze(A, fail), \+ bind(A),
                freeze(B, C = done), bind(B), C == done,
                freeze(D, member(Z, [1, 2])), D = x, Z == 2, !,
                ( freeze(E, fail), E = 1 -> fail ; true ),
                freeze(F, nb_setval(global_woke, yes)), nb_setval(saved, f(F)),
                nb_getval(saved, f(H)), H = go, nb_getval(global_woke, yes).
            mixed :- freeze(X, Y = woke), runtime_bind(X), Y == woke.
            cut_cleanup :-
                setup_call_cleanup(true, member(_, [1, 2]), nb_setval(cut_done, yes)), !,
                nb_getval(cut_done, yes).
            """ + "\n",
            "frozen.pl",
            Runtime.PrologLanguageMode.Modern
        );

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Frozen.FrozenModule")!;
        using var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow("runtime_bind(1).", "runtime.pl");

        Assert.Equal(true, Call(module, type, "Check"));
        Assert.Equal(true, Call(module, type, "Mixed"));
        Assert.Equal(true, Call(module, type, "CutCleanup"));
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void OccursCheckModeBoundaryHoldsAcrossGeneratedAndConsultedCalls(Runtime.PrologLanguageMode mode)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('OccursCheck').
            :- clr_namespace('Generated.OccursCheck').
            :- clr_export(check/0, semidet, []).
            """,
            "Generated.OccursCheck",
            "occurs_check.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        var behavior =
            mode == Runtime.PrologLanguageMode.StrictIso
                ? """
                    catch(set_prolog_flag(occurs_check, error), error(E, _), true),
                    E == domain_error(prolog_flag, occurs_check)
                    """
                : """
                    set_prolog_flag(occurs_check, error),
                    catch(-X = X, error(E, C), true),
                    E == representation_error(term), C = occurs_check(V, T),
                    var(V), T == -V, var(X)
                    """;
        var body = behavior + ", \\+ unify_with_occurs_check(Y, -Y), var(Y).";
        var source = FacadeGenerator.Generate(
            contract.Contract!,
            "check :- " + body + "\ncompiled_to_runtime :- runtime_check.\n",
            "occurs_check.pl",
            mode
        );
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.OccursCheck.OccursCheckModule")!;
        using var engine = new Compiler.PrologEngine(mode);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow("runtime_check :- " + body + "\nruntime_to_compiled :- check.\n", "runtime.pl");

        Assert.Equal(true, Call(module, type, "Check"));
        foreach (var goal in new[] { "check", "runtime_check", "compiled_to_runtime", "runtime_to_compiled" })
        {
            Assert.Single(engine.Query(goal).Solutions());
        }
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void CyclicErrorCulpritsSurviveGeneratedAndConsultedCatchers(Runtime.PrologLanguageMode mode)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('CyclicErrors').
            :- clr_namespace('Generated.CyclicErrors').
            :- clr_export(check/0, semidet, []).
            :- clr_export(mixed/0, semidet, []).
            """,
            "Generated.CyclicErrors",
            "cyclic_errors.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        var source = FacadeGenerator.Generate(
            contract.Contract!,
            """
            raise_chars :- L = ['1'|L], number_chars(_, L).
            check :-
                catch(raise_chars, error(type_error(list, C), _), true),
                nonvar(C), C = ['1'|T], C == T,
                catch((L = [49|L], number_codes(1, L)), error(type_error(list, D), _), true),
                var(L), nonvar(D), D = [49|DT], D == DT.
            mixed :-
                catch(runtime_raise, error(type_error(list, C), _), true),
                nonvar(C), C = [49|T], C == T.
            """ + "\n",
            "cyclic_errors.pl",
            mode
        );
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.CyclicErrors.CyclicErrorsModule")!;
        using var engine = new Compiler.PrologEngine(mode);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow(
            """
            runtime_raise :- L = [49|L], number_codes(_, L).
            runtime_catch :-
                catch(raise_chars, error(type_error(list, C), _), true),
                nonvar(C), C = ['1'|T], C == T.
            """ + "\n",
            "runtime.pl"
        );

        Assert.Equal(true, Call(module, type, "Check"));
        Assert.Equal(true, Call(module, type, "Mixed"));
        Assert.True(engine.Query("runtime_catch").Prove());
    }

    [Theory]
    [InlineData(Runtime.PrologLanguageMode.Modern)]
    [InlineData(Runtime.PrologLanguageMode.StrictIso)]
    public void NumericCharacterSyntaxErrorsCrossGeneratedAndConsultedCalls(Runtime.PrologLanguageMode mode)
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('NumericSyntax').
            :- clr_namespace('Generated.NumericSyntax').
            :- clr_export(check/0, semidet, []).
            """,
            "Generated.NumericSyntax",
            "numeric_syntax.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        const string body = """
            atom_codes(A, [48,39,39]), atom_chars(A, Chars),
            catch(number_chars(N, Chars), error(syntax_error(_), _), Quote = yes),
            Quote == yes, var(N),
            catch(number_codes(0, [48,39,92,48]), error(syntax_error(_), _), Zero = yes),
            Zero == yes,
            number_codes(39, [48,39,39,39]), number_codes(0, [48,39,92,48,92]).
            """;
        var source = FacadeGenerator.Generate(
            contract.Contract!,
            "check :- " + body + "\ncompiled_to_runtime :- runtime_check.\n",
            "numeric_syntax.pl",
            mode
        );
        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.NumericSyntax.NumericSyntaxModule")!;
        using var engine = new Compiler.PrologEngine(mode);
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow("runtime_check :- " + body + "\nruntime_to_compiled :- check.\n", "runtime.pl");

        Assert.Equal(true, Call(module, type, "Check"));
        foreach (var goal in new[] { "check", "runtime_check", "compiled_to_runtime", "runtime_to_compiled" })
        {
            Assert.Single(engine.Query(goal).Solutions());
        }
    }

    [Fact]
    public void FacadeCarriesStringConstantsThroughGeneratedCode()
    {
        var contract = ContractReader.Read(
            """
            :- clr_module('Stringy').
            :- clr_namespace('Generated.Stringy').
            :- clr_export(check/0, semidet, []).
            """,
            "Generated.Stringy",
            "stringy.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        var source = FacadeGenerator.Generate(
            contract.Contract!,
            """
            :- set_prolog_flag(double_quotes, string).
            str_text("hello").
            check :- str_text(S), string(S), string_length(S, 5), string_concat(S, "!", "hello!").
            """,
            "stringy.pl",
            Runtime.PrologLanguageMode.Modern
        );

        // The build-time constant reaches the generated installer as a string cell.
        Assert.Contains("Cell.String(symbols.InternAtom(\"hello\"))", source, StringComparison.Ordinal);

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Generated.Stringy.StringyModule")!;
        var module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal(true, Call(module, type, "Check"));
    }

    [Fact]
    public void EntryPointCarriesTheFlagOverrideIntoItsEngine()
    {
        var source = EntryPointGenerator.Generate(
            "Generated.App",
            [("app.pl", ":- initialization(true).")],
            Runtime.PrologLanguageMode.Modern,
            CodesOverride
        );

        Assert.Contains("DoubleQuotes = global::DotProlog.Runtime.DoubleQuotesMode.Codes", source, StringComparison.Ordinal);
        Assert.Contains("requires double_quotes to start at codes", source, StringComparison.Ordinal);
    }
}
