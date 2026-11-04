using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class ContractOutputLintCorpusTests
{
    private const int CorpusVersion = 2;

    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.ContractOutputTypeMismatch],
    };

    private static readonly CorpusCase[] Cases =
    [
        Output("atom-from-atom", "p(ok).", "atom", 0),
        Output("atom-from-fixnum", "p(42).", "atom", 1),
        Output("atom-from-big-integer", "p(9223372036854775808).", "atom", 1),
        Output("atom-from-float", "p(1.5).", "atom", 1),
        Output("atom-from-rational", "p(1r2).", "atom", 1),
        Output("atom-from-normalized-rational", "p(2r1).", "atom", 1),
        Output("atom-from-compound", "p(foo(bar)).", "atom", 1),
        Output("atom-from-list", "p([a,b]).", "atom", 1),
        Output("atom-from-improper-list", "p([a|b]).", "atom", 1),
        Output("integer-from-atom", "p(ok).", "integer", 1),
        Output("integer-from-fixnum", "p(42).", "integer", 0),
        Output("integer-from-negative-fixnum", "p(-42).", "integer", 0),
        Output("integer-from-big-integer", "p(9223372036854775808).", "integer", 1),
        Output("integer-from-widened-integer", "p(576460752303423488).", "integer", 1),
        Output("integer-from-float", "p(1.5).", "integer", 1),
        Output("integer-from-rational", "p(1r2).", "integer", 1),
        Output("integer-from-normalized-rational", "p(2r1).", "integer", 0),
        Output("integer-from-compound", "p(foo(bar)).", "integer", 1),
        Output("integer-from-list", "p([a,b]).", "integer", 1),
        Output("integer-from-improper-list", "p([a|b]).", "integer", 1),
        Output("float-from-atom", "p(ok).", "float", 1),
        Output("float-from-fixnum", "p(42).", "float", 0),
        Output("float-from-big-integer", "p(9223372036854775808).", "float", 0),
        Output("float-from-float", "p(1.5).", "float", 0),
        Output("float-from-rational", "p(1r2).", "float", 0),
        Output("float-from-normalized-rational", "p(2r1).", "float", 0),
        Output("float-from-compound", "p(foo(bar)).", "float", 1),
        Output("float-from-list", "p([a,b]).", "float", 1),
        Output("float-from-improper-list", "p([a|b]).", "float", 1),
        Output("term-from-atom", "p(ok).", "term", 0),
        Output("term-from-fixnum", "p(42).", "term", 0),
        Output("term-from-big-integer", "p(9223372036854775808).", "term", 0),
        Output("term-from-float", "p(1.5).", "term", 0),
        Output("term-from-rational", "p(1r2).", "term", 0),
        Output("term-from-normalized-rational", "p(2r1).", "term", 0),
        Output("term-from-compound", "p(foo(bar)).", "term", 0),
        Output("term-from-list", "p([a,b]).", "term", 0),
        Output("term-from-improper-list", "p([a|b]).", "term", 0),
        Output("list-from-atom", "p(ok).", "list(atom)", 1),
        Output("list-from-fixnum", "p(42).", "list(atom)", 1),
        Output("list-from-big-integer", "p(9223372036854775808).", "list(atom)", 1),
        Output("list-from-float", "p(1.5).", "list(atom)", 1),
        Output("list-from-rational", "p(1r2).", "list(atom)", 1),
        Output("list-from-normalized-rational", "p(2r1).", "list(atom)", 1),
        Output("list-from-compound", "p(foo(bar)).", "list(atom)", 1),
        Output("list-from-empty-list", "p([]).", "list(atom)", 0),
        Output("list-from-proper-list", "p([a,b]).", "list(atom)", 0),
        Output("list-from-improper-list", "p([a|b]).", "list(atom)", 1),
        Output("list-from-ground-improper-tail", "p([a|1]).", "list(atom)", 1),
        Output("list-from-wrong-element", "p([a,1]).", "list(atom)", 1),
        Output("list-from-big-integer-element", "p([9223372036854775808]).", "list(integer)", 1),
        Output("list-from-numeric-elements", "p([1,1.5,1r2]).", "list(float)", 0),
        Output("list-from-ground-compound-elements", "p([foo(bar),1,a]).", "list(term)", 0),
        Output("nested-list-consistent", "p([[a],[b]]).", "list(list(atom))", 0),
        Output("nested-list-element-violation", "p([[a],[1]]).", "list(list(atom))", 1),
        Output("nested-improper-list-violation", "p([[a|b]]).", "list(list(atom))", 1),
        Output("semidet-output-violation", "p(wrong).", "integer", 1, determinism: "semidet"),
        Output("multi-output-violation", "p(wrong).", "integer", 1, determinism: "multi"),
        Output("nondet-output-violation", "p(wrong).", "integer", 1, determinism: "nondet"),
        Output("strict-atom-output-violation", "p(wrong).", "integer", 1, PrologLanguageMode.StrictIso),
        Output("strict-integer-output-consistent", "p(42).", "integer", 0, PrologLanguageMode.StrictIso),
        Output("strict-list-element-violation", "p([a,1]).", "list(atom)", 1, PrologLanguageMode.StrictIso),
        Invocation("atom-input-reaches-output", "ok", "atom", 1),
        Invocation("integer-input-reaches-output", "42", "integer", 1),
        Invocation("float-input-reaches-output", "1.5", "float", 1),
        Invocation("term-input-reaches-output", "foo(bar)", "term", 1),
        Invocation("list-input-reaches-output", "[a,b]", "list(atom)", 1),
        Invocation("nested-list-input-reaches-output", "[[a],[b]]", "list(list(atom))", 1),
        Invocation("integer-input-is-not-atom", "1", "atom", 0),
        Invocation("float-input-is-not-integer", "1.0", "integer", 0),
        Invocation("big-input-is-not-64-bit-integer", "9223372036854775808", "integer", 0),
        Invocation("widened-64-bit-input-reaches-output", "576460752303423488", "integer", 1),
        Invocation("integer-input-has-no-float-coercion", "1", "float", 0),
        Invocation("rational-input-has-no-float-coercion", "1r2", "float", 0),
        Invocation("improper-list-input-is-unknown", "[a|b]", "list(atom)", 0),
        Invocation("wrong-list-element-input-is-unknown", "[1]", "list(atom)", 0),
        Invocation("variable-input-is-unknown", "X", "atom", 0),
        Invocation("partially-ground-input-is-unknown", "foo(X)", "term", 0),
        Invocation("open-list-input-is-unknown", "[a|X]", "list(atom)", 0),
        Invocation("quoted-input-is-reader-dependent", "\"text\"", "atom", 0),
        Invocation("strict-integer-input-has-no-float-coercion", "1", "float", 0, PrologLanguageMode.StrictIso),
        Custom(
            "aliased-inputs-are-not-solved",
            [Unit("corpus.pl", ":- module(m, [p/3]). p(X, X, wrong).")],
            "p/3, det, [in(a, atom), in(b, integer), out(value, integer)]",
            0
        ),
        Output("variable-output-is-unknown", "p(X).", "integer", 0),
        Output("compound-variable-output-is-unknown", "p(foo(X)).", "term", 0),
        Output("open-list-output-is-unknown", "p([a|X]).", "list(atom)", 0),
        Output("open-list-term-output-is-unknown", "p([a|X]).", "term", 0),
        Output("quoted-output-is-reader-dependent", "p(\"text\").", "integer", 0),
        Output("later-fact-is-unknown", "p(1). p(wrong).", "integer", 0),
        Output("leading-rule-is-unknown", "p(wrong) :- true.", "integer", 0),
        Output("leading-cut-rule-is-unknown", "p(wrong) :- !.", "integer", 0),
        Output("missing-clause-is-unknown", "", "integer", 0),
        Custom(
            "leading-grammar-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p --> [].")],
            "p/2, det, [out(left, integer), out(right, integer)]",
            0
        ),
        Custom(
            "ordinary-then-grammar-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(ok, 1). p --> [].")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Custom(
            "grammar-then-ordinary-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p --> []. p(ok, wrong).")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Output("dynamic-target-is-unknown", ":- dynamic(p/1). p(wrong).", "integer", 0),
        Output("multifile-target-is-unknown", ":- multifile(p/1). p(wrong).", "integer", 0),
        Output("initialization-effect-is-unknown", ":- initialization(halt(9)). p(wrong).", "integer", 0),
        Output("database-effect-is-unknown", ":- assertz(q(1)). p(wrong).", "integer", 0),
        Output("source-loading-effect-is-unknown", ":- ensure_loaded('missing.pl'). p(wrong).", "integer", 0),
        Output("operator-effect-is-unknown", ":- op(500, xfy, custom). p(wrong).", "integer", 0),
        Output("flag-effect-is-unknown", ":- set_prolog_flag(double_quotes, atom). p(wrong).", "integer", 0),
        Custom(
            "imported-only-definition-is-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(wrong)."), Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl').")],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "reexported-only-definition-is-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(wrong)."), Unit("rules.pl", ":- module(m, [p/1]). :- reexport(a).")],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "ambiguous-export-modules-are-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(wrong)."), Unit("b.pl", ":- module(b, [p/1]). p(wrong).")],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "unexported-definition-is-unknown",
            [Unit("corpus.pl", ":- module(m, []). p(wrong).")],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "imported-effect-is-unknown",
            [
                Unit("a.pl", ":- module(a, [q/1]). :- initialization(halt(9)). q(1)."),
                Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl'). p(wrong)."),
            ],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "multiple-outputs-have-independent-results",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(wrong, 42).")],
            "p/2, det, [out(first, integer), out(second, integer)]",
            1
        ),
    ];

    [Fact]
    public void VersionedCorpusProducesOnlyIndependentlyExpectedFindings()
    {
        Assert.Equal(2, CorpusVersion);
        Assert.Equal(Cases.Length, Cases.Select(test => test.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (CorpusCase test in Cases)
        {
            var actual = Analyze(test).Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch);
            Assert.True(
                actual == test.ExpectedFindings,
                $"{test.Name}: expected {test.ExpectedFindings} DPL3052 findings, actual {actual}."
            );
        }
    }

    [Fact]
    public void EverySeededViolationIsDetected()
    {
        CorpusCase[] seeded = [.. Cases.Where(test => test.ExpectedFindings > 0)];
        Assert.NotEmpty(seeded);
        Assert.All(
            seeded,
            test =>
                Assert.Equal(
                    test.ExpectedFindings,
                    Analyze(test).Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch)
                )
        );
    }

    [Fact]
    public void EveryRepositoryContractIsFreeOfLeadingFactOutputViolations()
    {
        var root = RepositoryRoot();
        string[] contracts = Directory.GetFiles(root, "*.dpli", SearchOption.AllDirectories);
        Assert.NotEmpty(contracts);
        foreach (var contractPath in contracts)
        {
            var sourcePath = Path.ChangeExtension(contractPath, ".pl");
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            ContractReadResult contract = ContractReader.Read(File.ReadAllText(contractPath), "Corpus", contractPath);
            Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
                sourcePath,
                File.ReadAllText(sourcePath),
                new BytecodeProgram()
            );
            IReadOnlyList<Diagnostic> diagnostics = PrologContractLinter.Analyze(
                [loaded.Source],
                contract.Contract!,
                contractPath,
                Enabled
            );
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch);
        }
    }

    private static CorpusCase Output(
        string name,
        string clauses,
        string type,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        string determinism = "det"
    ) =>
        Custom(
            name,
            [Unit("corpus.pl", $":- module(m, [p/1]). {clauses}")],
            $"p/1, {determinism}, [out(value, {type})]",
            expectedFindings,
            mode
        );

    private static CorpusCase Invocation(
        string name,
        string input,
        string inputType,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern
    ) =>
        Custom(
            name,
            [Unit("corpus.pl", $":- module(m, [p/2]). p({input}, wrong).")],
            $"p/2, det, [in(item, {inputType}), out(value, integer)]",
            expectedFindings,
            mode
        );

    private static CorpusCase Custom(
        string name,
        CorpusSource[] sources,
        string export,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern
    ) => new(name, sources, export, expectedFindings, mode);

    private static CorpusSource Unit(string fileName, string text) => new(fileName, text);

    private static IReadOnlyList<Diagnostic> Analyze(CorpusCase test)
    {
        PrologLintSource[] sources =
        [
            .. test.Sources.Select(source =>
                PrologLintSourceLoader
                    .Load(Path.Combine(Path.GetTempPath(), source.FileName), source.Text, new BytecodeProgram(test.Mode))
                    .Source
            ),
        ];
        ContractReadResult contract = ContractReader.Read(
            $":- clr_module('M'). :- clr_export({test.Export}).",
            "Corpus",
            "corpus.dpli"
        );
        Assert.True(contract.Success, $"{test.Name}: {string.Join("; ", contract.Diagnostics)}");
        return PrologContractLinter.Analyze(sources, contract.Contract!, "corpus.dpli", Enabled);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed record CorpusCase(
        string Name,
        CorpusSource[] Sources,
        string Export,
        int ExpectedFindings,
        PrologLanguageMode Mode
    );

    private sealed record CorpusSource(string FileName, string Text);
}
