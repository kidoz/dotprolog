using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class LaterContractOutputLintCorpusTests
{
    private const int CorpusVersion = 1;

    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.LaterContractOutputTypeMismatch],
    };

    private static readonly CorpusCase[] Cases =
    [
        Output("atom-from-atom", "p(ok). p(ok).", "atom", 0),
        Output("atom-from-fixnum", "p(ok). p(42).", "atom", 1),
        Output("atom-from-big-integer", "p(ok). p(9223372036854775808).", "atom", 1),
        Output("atom-from-float", "p(ok). p(1.5).", "atom", 1),
        Output("atom-from-rational", "p(ok). p(1r2).", "atom", 1),
        Output("atom-from-normalized-rational", "p(ok). p(2r1).", "atom", 1),
        Output("atom-from-compound", "p(ok). p(foo(bar)).", "atom", 1),
        Output("atom-from-list", "p(ok). p([a,b]).", "atom", 1),
        Output("atom-from-improper-list", "p(ok). p([a|b]).", "atom", 1),
        Output("integer-from-atom", "p(1). p(ok).", "integer", 1),
        Output("integer-from-fixnum", "p(1). p(42).", "integer", 0),
        Output("integer-from-negative-fixnum", "p(1). p(-42).", "integer", 0),
        Output("integer-from-big-integer", "p(1). p(9223372036854775808).", "integer", 1),
        Output("integer-from-widened-integer", "p(1). p(576460752303423488).", "integer", 1),
        Output("integer-from-float", "p(1). p(1.5).", "integer", 1),
        Output("integer-from-rational", "p(1). p(1r2).", "integer", 1),
        Output("integer-from-normalized-rational", "p(1). p(2r1).", "integer", 0),
        Output("integer-from-compound", "p(1). p(foo(bar)).", "integer", 1),
        Output("integer-from-list", "p(1). p([a,b]).", "integer", 1),
        Output("integer-from-improper-list", "p(1). p([a|b]).", "integer", 1),
        Output("float-from-atom", "p(1.0). p(ok).", "float", 1),
        Output("float-from-fixnum", "p(1.0). p(42).", "float", 0),
        Output("float-from-big-integer", "p(1.0). p(9223372036854775808).", "float", 0),
        Output("float-from-float", "p(1.0). p(1.5).", "float", 0),
        Output("float-from-rational", "p(1.0). p(1r2).", "float", 0),
        Output("float-from-normalized-rational", "p(1.0). p(2r1).", "float", 0),
        Output("float-from-compound", "p(1.0). p(foo(bar)).", "float", 1),
        Output("float-from-list", "p(1.0). p([a,b]).", "float", 1),
        Output("float-from-improper-list", "p(1.0). p([a|b]).", "float", 1),
        Output("term-from-atom", "p(ok). p(ok).", "term", 0),
        Output("term-from-fixnum", "p(ok). p(42).", "term", 0),
        Output("term-from-big-integer", "p(ok). p(9223372036854775808).", "term", 0),
        Output("term-from-float", "p(ok). p(1.5).", "term", 0),
        Output("term-from-rational", "p(ok). p(1r2).", "term", 0),
        Output("term-from-normalized-rational", "p(ok). p(2r1).", "term", 0),
        Output("term-from-compound", "p(ok). p(foo(bar)).", "term", 0),
        Output("term-from-list", "p(ok). p([a,b]).", "term", 0),
        Output("term-from-improper-list", "p(ok). p([a|b]).", "term", 0),
        Output("list-from-atom", "p([]). p(ok).", "list(atom)", 1),
        Output("list-from-fixnum", "p([]). p(42).", "list(atom)", 1),
        Output("list-from-big-integer", "p([]). p(9223372036854775808).", "list(atom)", 1),
        Output("list-from-float", "p([]). p(1.5).", "list(atom)", 1),
        Output("list-from-rational", "p([]). p(1r2).", "list(atom)", 1),
        Output("list-from-normalized-rational", "p([]). p(2r1).", "list(atom)", 1),
        Output("list-from-compound", "p([]). p(foo(bar)).", "list(atom)", 1),
        Output("list-from-empty-list", "p([]). p([]).", "list(atom)", 0),
        Output("list-from-proper-list", "p([]). p([a,b]).", "list(atom)", 0),
        Output("list-from-improper-list", "p([]). p([a|b]).", "list(atom)", 1),
        Output("list-from-ground-improper-tail", "p([]). p([a|1]).", "list(atom)", 1),
        Output("list-from-wrong-element", "p([]). p([a,1]).", "list(atom)", 1),
        Output("list-from-big-integer-element", "p([]). p([9223372036854775808]).", "list(integer)", 1),
        Output("list-from-numeric-elements", "p([]). p([1,1.5,1r2]).", "list(float)", 0),
        Output("list-from-ground-compound-elements", "p([]). p([foo(bar),1,a]).", "list(term)", 0),
        Output("nested-list-consistent", "p([]). p([[a],[b]]).", "list(list(atom))", 0),
        Output("nested-list-element-violation", "p([]). p([[a],[1]]).", "list(list(atom))", 1),
        Output("nested-improper-list-violation", "p([]). p([[a|b]]).", "list(list(atom))", 1),
        Output("det-no-input-hides-later", "p(1). p(wrong).", "integer", 0, determinism: "det"),
        Output("semidet-no-input-hides-later", "p(1). p(wrong).", "integer", 0, determinism: "semidet"),
        Output("multi-no-input-reaches-later", "p(1). p(wrong).", "integer", 1, determinism: "multi"),
        Output("nondet-no-input-reaches-later", "p(1). p(wrong).", "integer", 1, determinism: "nondet"),
        Invocation("det-different-input-reaches-later", "p(1, 1). p(2, wrong).", "integer", 1),
        Invocation("det-equal-input-hides-later", "p(1, 1). p(1, wrong).", "integer", 0),
        Invocation("semidet-different-input-reaches-later", "p(1, 1). p(2, wrong).", "integer", 1, determinism: "semidet"),
        Invocation("semidet-equal-input-hides-later", "p(1, 1). p(1, wrong).", "integer", 0, determinism: "semidet"),
        Invocation("multi-equal-input-reaches-later", "p(1, 1). p(1, wrong).", "integer", 1, determinism: "multi"),
        Invocation("multi-different-input-reaches-later", "p(1, 1). p(2, wrong).", "integer", 1, determinism: "multi"),
        Invocation("nondet-equal-input-reaches-later", "p(1, 1). p(1, wrong).", "integer", 1, determinism: "nondet"),
        Invocation("nondet-different-input-reaches-later", "p(1, 1). p(2, wrong).", "integer", 1, determinism: "nondet"),
        Invocation("det-nearer-fact-hides-third", "p(1, 1). p(2, 2). p(2, wrong).", "integer", 0),
        Invocation("det-distinct-third-is-reachable", "p(1, 1). p(2, 2). p(3, wrong).", "integer", 1),
        Invocation("det-earlier-incompatible-output-still-hides", "p(1, wrong). p(1, also_wrong).", "integer", 0),
        Invocation("atom-input-reaches-later", "p(a, 1). p(b, wrong).", "atom", 1),
        Invocation("integer-input-reaches-later", "p(1, 1). p(2, wrong).", "integer", 1),
        Invocation("float-input-reaches-later", "p(1.0, 1). p(2.0, wrong).", "float", 1),
        Invocation("term-input-reaches-later", "p(foo(a), 1). p(foo(b), wrong).", "term", 1),
        Invocation("list-input-reaches-later", "p([a], 1). p([b], wrong).", "list(atom)", 1),
        Invocation("nested-list-input-reaches-later", "p([[a]], 1). p([[b]], wrong).", "list(list(atom))", 1),
        Invocation("widened-64-bit-input-reaches-later", "p(1, 1). p(576460752303423488, wrong).", "integer", 1),
        Invocation("integer-input-is-not-atom", "p(a, 1). p(1, wrong).", "atom", 0),
        Invocation("float-input-is-not-integer", "p(1.0, 1). p(2, wrong).", "integer", 0),
        Invocation("later-float-input-is-not-integer", "p(1, 1). p(2.0, wrong).", "integer", 0),
        Invocation("big-input-is-not-64-bit-integer", "p(1, 1). p(9223372036854775808, wrong).", "integer", 0),
        Invocation("integer-input-has-no-float-coercion", "p(1.0, 1). p(2, wrong).", "float", 0),
        Invocation("rational-input-has-no-float-coercion", "p(1r2, 1). p(2.0, wrong).", "float", 0),
        Invocation("improper-list-input-is-unknown", "p([a], 1). p([b|c], wrong).", "list(atom)", 0),
        Invocation("wrong-list-element-input-is-unknown", "p([a], 1). p([1], wrong).", "list(atom)", 0),
        Invocation("variable-input-is-unknown", "p(a, 1). p(X, wrong).", "atom", 0),
        Invocation("partially-ground-input-is-unknown", "p(foo(a), 1). p(foo(X), wrong).", "term", 0),
        Invocation("open-list-input-is-unknown", "p([a], 1). p([b|X], wrong).", "list(atom)", 0),
        Invocation("quoted-input-is-reader-dependent", "p(a, 1). p(\"text\", wrong).", "atom", 0),
        Invocation(
            "strict-integer-input-has-no-float-coercion",
            "p(1.0, 1). p(2, wrong).",
            "float",
            0,
            PrologLanguageMode.StrictIso
        ),
        Invocation("normalized-rational-unifies-with-integer", "p(1, 1). p(1r1, wrong).", "term", 0),
        Invocation("float-does-not-unify-with-integer", "p(1, 1). p(1.0, wrong).", "term", 1),
        Invocation("equal-big-integers-unify", "p(9223372036854775808, 1). p(9223372036854775808, wrong).", "term", 0),
        Invocation("different-big-integers-do-not-unify", "p(9223372036854775808, 1). p(9223372036854775809, wrong).", "term", 1),
        Invocation("equal-compounds-unify", "p(foo(a), 1). p(foo(a), wrong).", "term", 0),
        Invocation("different-compounds-do-not-unify", "p(foo(a), 1). p(foo(b), wrong).", "term", 1),
        Invocation("equal-lists-unify", "p([a,b], 1). p([a,b], wrong).", "term", 0),
        Invocation("different-lists-do-not-unify", "p([a,b], 1). p([a,c], wrong).", "term", 1),
        Invocation("equal-ground-improper-lists-unify", "p([a|b], 1). p([a|b], wrong).", "term", 0),
        Invocation("different-ground-improper-lists-do-not-unify", "p([a|b], 1). p([a|c], wrong).", "term", 1),
        Output("strict-later-atom-violation", "p(1). p(wrong).", "integer", 1, PrologLanguageMode.StrictIso),
        Output("strict-later-integer-consistent", "p(1). p(42).", "integer", 0, PrologLanguageMode.StrictIso),
        Output("strict-later-list-element-violation", "p([]). p([a,1]).", "list(atom)", 1, PrologLanguageMode.StrictIso),
        Output("leading-rule-is-unknown", "p(1) :- true. p(wrong).", "integer", 0),
        Output("later-rule-is-unknown", "p(1). p(wrong) :- true.", "integer", 0),
        Output("leading-cut-rule-is-unknown", "p(1) :- !. p(wrong).", "integer", 0),
        Output("single-fact-has-no-later-candidate", "p(wrong).", "integer", 0),
        Output("variable-output-is-unknown", "p(X). p(wrong).", "integer", 0),
        Output("compound-variable-output-is-unknown", "p(foo(X)). p(wrong).", "term", 0),
        Output("open-list-output-is-unknown", "p([a|X]). p(wrong).", "list(atom)", 0),
        Output("quoted-output-is-reader-dependent", "p(\"text\"). p(wrong).", "integer", 0),
        Output("dynamic-target-is-unknown", ":- dynamic(p/1). p(1). p(wrong).", "integer", 0),
        Output("multifile-target-is-unknown", ":- multifile(p/1). p(1). p(wrong).", "integer", 0),
        Output("initialization-effect-is-unknown", ":- initialization(halt(9)). p(1). p(wrong).", "integer", 0),
        Output("database-effect-is-unknown", ":- assertz(q(1)). p(1). p(wrong).", "integer", 0),
        Output("source-loading-effect-is-unknown", ":- ensure_loaded('missing.pl'). p(1). p(wrong).", "integer", 0),
        Output("operator-effect-is-unknown", ":- op(500, xfy, custom). p(1). p(wrong).", "integer", 0),
        Output("flag-effect-is-unknown", ":- set_prolog_flag(double_quotes, atom). p(1). p(wrong).", "integer", 0),
        Custom(
            "ordinary-then-grammar-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(ok, 1). p --> []. p(2, wrong).")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Custom(
            "grammar-then-ordinary-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p --> []. p(ok, 1). p(2, wrong).")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Custom(
            "imported-only-definition-is-unknown",
            [
                Unit("a.pl", ":- module(a, [p/1]). p(1). p(wrong)."),
                Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl')."),
            ],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "reexported-only-definition-is-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(1). p(wrong)."), Unit("rules.pl", ":- module(m, [p/1]). :- reexport(a).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "ambiguous-export-modules-are-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(1). p(wrong)."), Unit("b.pl", ":- module(b, [p/1]). p(1). p(wrong).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "unexported-definition-is-unknown",
            [Unit("corpus.pl", ":- module(m, []). p(1). p(wrong).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "imported-effect-is-unknown",
            [
                Unit("a.pl", ":- module(a, [q/1]). :- initialization(halt(9)). q(1)."),
                Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl'). p(1). p(wrong)."),
            ],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "multiple-later-outputs-have-independent-results",
            [Unit("corpus.pl", ":- module(m, [p/3]). p(1, 1, 1). p(2, wrong, also_wrong).")],
            "p/3, det, [in(item, integer), out(first, integer), out(second, integer)]",
            2
        ),
    ];

    [Fact]
    public void VersionedCorpusProducesOnlyIndependentlyExpectedFindings()
    {
        Assert.Equal(1, CorpusVersion);
        Assert.Equal(Cases.Length, Cases.Select(test => test.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (CorpusCase test in Cases)
        {
            var actual = Analyze(test).Count(diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch);
            Assert.True(
                actual == test.ExpectedFindings,
                $"{test.Name}: expected {test.ExpectedFindings} DPL3053 findings, actual {actual}."
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
                    Analyze(test).Count(diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch)
                )
        );
    }

    [Fact]
    public void EveryRepositoryContractIsFreeOfLaterFactOutputViolations()
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
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch);
        }
    }

    private static CorpusCase Output(
        string name,
        string clauses,
        string type,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        string determinism = "nondet"
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
        string clauses,
        string inputType,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        string determinism = "det"
    ) =>
        Custom(
            name,
            [Unit("corpus.pl", $":- module(m, [p/2]). {clauses}")],
            $"p/2, {determinism}, [in(item, {inputType}), out(value, integer)]",
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
