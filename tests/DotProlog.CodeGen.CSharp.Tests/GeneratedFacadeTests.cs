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
    private static readonly string[] SplitInput = ["a", "b"];

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
        """;

    private static ModuleContract ReadContract()
    {
        ContractReadResult result = ContractReader.Read(Contract, "Fallback", "pricing.dpli");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.Contract!;
    }

    private static object CreateModule(out Type moduleType)
    {
        var source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");
        Assembly assembly = CompileGenerated(source);
        moduleType = assembly.GetType("Generated.Pricing.PricingModule")!;
        Assert.NotNull(moduleType);

        return moduleType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;
    }

    private static Assembly CompileGenerated(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            $"GeneratedFacade_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var image = new MemoryStream();
        EmitResult emitted = compilation.Emit(image);

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
            MetadataReference.CreateFromFile(typeof(Runtime.Machine).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Compiler.PrologEngine).Assembly.Location),
        ];
    }

    private static object? Call(object module, Type type, string method, params object?[] arguments) =>
        type.GetMethod(method)!.Invoke(module, arguments);

    [Fact]
    public void GeneratedFacadeCompilesAndLoads()
    {
        var module = CreateModule(out Type type);

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
        var module = CreateModule(out Type type);

        Assert.Equal(90.0, Call(module, type, "Discount", 100.0, 10L));
    }

    [Fact]
    public void SemiDeterministicExportWithNoOutputsReturnsBool()
    {
        var module = CreateModule(out Type type);

        Assert.Equal(true, Call(module, type, "InStock", "widget"));
        Assert.Equal(false, Call(module, type, "InStock", "sprocket"));
    }

    [Fact]
    public void SemiDeterministicExportWithAnOutputBecomesATryMethod()
    {
        var module = CreateModule(out Type type);
        MethodInfo tryStockLevel = type.GetMethod("TryStockLevel")!;

        object?[] found = ["widget", null];
        Assert.Equal(true, tryStockLevel.Invoke(module, found));
        Assert.Equal(7L, found[1]);

        object?[] missing = ["gadget", null];
        Assert.Equal(false, tryStockLevel.Invoke(module, missing));
    }

    [Fact]
    public void NondeterministicExportStreamsEverySolution()
    {
        var module = CreateModule(out Type type);

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
        var module = CreateModule(out Type type);

        var values = (IEnumerable<string>)Call(module, type, "Audited", CancellationToken.None)!;

        Assert.Equal(["one", "two"], values);
    }

    [Fact]
    public void NondeterministicExportWithNoOutputsStreamsAUnitPerSolution()
    {
        var module = CreateModule(out Type type);

        var found = (IEnumerable<Runtime.Unit>)Call(module, type, "Catalogued", "widget", CancellationToken.None)!;
        Assert.Single(found);

        var missing = (IEnumerable<Runtime.Unit>)Call(module, type, "Catalogued", "sprocket", CancellationToken.None)!;
        Assert.Empty(missing);
    }

    [Fact]
    public void ListsCrossTheBoundaryInBothDirections()
    {
        var module = CreateModule(out Type type);

        var splits = (System.Collections.IEnumerable)Call(module, type, "Split", SplitInput, CancellationToken.None)!;
        List<string> shapes = [];

        foreach (var split in splits)
        {
            Type resultType = split.GetType();
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
        var unused = CreateModule(out Type type);
        _ = unused;
        var engine = new Compiler.PrologEngine();
        var module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;
        engine.ConsultOrThrow("runtime_value(alpha). runtime_value(beta).", "runtime.pl");

        var values = (IEnumerable<string>)Call(module, type, "RuntimeBridge", CancellationToken.None)!;

        Assert.Equal(["alpha", "beta"], values);
    }

    [Fact]
    public void CompiledGrammarRulesCrossBothExecutionBoundaries()
    {
        var module = CreateModule(out Type type);

        Assert.Equal(true, Call(module, type, "GrammarAccepts", "token"));
        Assert.Equal(true, Call(module, type, "GrammarLooksAhead", "token"));
    }

    [Fact]
    public void ConsultedPredicateCallsACompiledPredicate()
    {
        var unused = CreateModule(out Type type);
        _ = unused;
        var engine = new Compiler.PrologEngine();
        type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine]);
        engine.ConsultOrThrow("consulted_colour(X) :- colour(X).", "runtime.pl");
        var host = new Runtime.PrologHost(engine.Machine);
        Runtime.PrologPredicate predicate = host.Bind("consulted_colour", 1);

        var values = host.CallAll(predicate, Runtime.PrologInput.Output)
            .Select(outputs => Runtime.PrologMarshal.ToAtom(outputs[0]))
            .ToArray();

        Assert.Equal(["red", "green", "blue"], values);
    }

    [Fact]
    public void ExecutableDirectiveRunsAgainstItsCompiledSourcePosition()
    {
        var unused = CreateModule(out Type type);
        _ = unused;
        using var output = new StringWriter();
        var engine = new Compiler.PrologEngine { Output = output };

        type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine]);

        Assert.Equal("prepared", output.ToString());
    }

    [Fact]
    public void CompiledDynamicPredicateKeepsItsLogicalDatabase()
    {
        var unused = CreateModule(out Type type);
        _ = unused;
        var engine = new Compiler.PrologEngine();
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
        ContractReadResult contract = ContractReader.Read(
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

    [Fact]
    public void StrictFacadeCompilesRunsAndRequiresAStrictEngine()
    {
        ContractReadResult contract = ContractReader.Read(
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
            "answer(42).",
            "strict.pl",
            Runtime.PrologLanguageMode.StrictIso
        );
        Assembly assembly = CompileGenerated(source);
        Type type = assembly.GetType("Generated.Strict.StrictModule")!;
        var module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal(true, Call(module, type, "Answer", 42L));

        TargetInvocationException mismatch = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [new Compiler.PrologEngine()])
        );
        Assert.Contains("requires StrictIso language mode", mismatch.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictFacadeRejectsAnExtensionAtBuildTime()
    {
        ContractReadResult contract = ContractReader.Read(
            """
            :- clr_module('Strict').
            :- clr_namespace('Generated.Strict').
            :- clr_export(answer/0, semidet, []).
            """,
            "Generated.Strict",
            "strict.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            FacadeGenerator.Generate(
                contract.Contract!,
                "answer :- member(a, [a]).",
                "strict.pl",
                Runtime.PrologLanguageMode.StrictIso
            )
        );

        Assert.Contains(Compiler.CompilerDiagnosticIds.StrictIsoViolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedStrictFacadePreservesIsoModuleContextMetadata()
    {
        ContractReadResult contract = ContractReader.Read(
            """
            :- clr_module('IsoContext').
            :- clr_namespace('Generated.IsoContext').
            :- clr_export(answer/1, det, [out(value, atom)]).
            """,
            "Generated.IsoContext",
            "iso-context.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        const string prolog = """
            :- module(contextual).
            :- export(answer/1).
            :- set_prolog_flag(double_quotes, chars).
            :- end_module(contextual).

            :- body(contextual).
            item(one).
            answer(Value) :- current_prolog_flag(double_quotes, chars), clause(item(Value), true).
            :- end_body(contextual).
            """;

        var source = FacadeGenerator.Generate(contract.Contract!, prolog, "iso-context.pl", Runtime.PrologLanguageMode.StrictIso);
        Assembly assembly = CompileGenerated(source);
        Type type = assembly.GetType("Generated.IsoContext.IsoContextModule")!;
        object module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal("one", Call(module, type, "Answer"));
    }

    [Fact]
    public void FacadeWithAFlagOverrideReadsAndRunsUnderTheOverride()
    {
        ContractReadResult contract = ContractReader.Read(
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
            Runtime.PrologLanguageMode.Extended,
            new Runtime.PrologFlagOverrides { DoubleQuotes = Runtime.DoubleQuotesMode.Atom }
        );

        Assembly assembly = CompileGenerated(source);
        Type type = assembly.GetType("Generated.Quoted.QuotedModule")!;
        object module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        // "hello" was read as one atom at build time, and Create() seeded the same override.
        Assert.Equal(true, Call(module, type, "Text", "hello"));

        TargetInvocationException mismatch = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [new Compiler.PrologEngine()])
        );
        Assert.Contains("requires double_quotes to start at atom", mismatch.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallRestoresDoubleQuotesAfterDirectiveReplay()
    {
        ContractReadResult contract = ContractReader.Read(
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
            Runtime.PrologLanguageMode.Extended
        );

        Assembly assembly = CompileGenerated(source);
        Type type = assembly.GetType("Generated.Replay.ReplayModule")!;
        var engine = new Compiler.PrologEngine();
        object module = type.GetMethod("Create", [typeof(Compiler.PrologEngine)])!.Invoke(null, [engine])!;

        // The source was one load unit: replaying its directive at install time must not leak the
        // flag into the host engine, matching what consulting the same file would leave behind.
        Assert.Equal(Runtime.DoubleQuotesMode.Codes, engine.Program.Flags.DoubleQuotes);
        Assert.Equal(true, Call(module, type, "CharsText", (object)SplitInput));
    }

    [Fact]
    public void FacadeCarriesStringConstantsThroughGeneratedCode()
    {
        ContractReadResult contract = ContractReader.Read(
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
            Runtime.PrologLanguageMode.Extended
        );

        // The build-time constant reaches the generated installer as a string cell.
        Assert.Contains("Cell.String(symbols.InternAtom(\"hello\"))", source, StringComparison.Ordinal);

        Assembly assembly = CompileGenerated(source);
        Type type = assembly.GetType("Generated.Stringy.StringyModule")!;
        object module = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!.Invoke(null, null)!;

        Assert.Equal(true, Call(module, type, "Check"));
    }

    [Fact]
    public void EntryPointCarriesTheFlagOverrideIntoItsEngine()
    {
        var source = EntryPointGenerator.Generate(
            "Generated.App",
            [("app.pl", ":- initialization(true).")],
            Runtime.PrologLanguageMode.Extended,
            new Runtime.PrologFlagOverrides { DoubleQuotes = Runtime.DoubleQuotesMode.Chars }
        );

        Assert.Contains("DoubleQuotes = global::DotProlog.Runtime.DoubleQuotesMode.Chars", source, StringComparison.Ordinal);
        Assert.Contains("requires double_quotes to start at chars", source, StringComparison.Ordinal);
    }
}
