using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class GroundRuleContractOutputLintCorpusTests : IDisposable
{
    private const int CorpusVersion = 1;

    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.GroundRuleContractOutputTypeMismatch],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-ground-rule-corpus-" + Guid.NewGuid().ToString("N")
    );

    private static readonly CorpusCase[] Cases =
    [
        Output("atom-from-atom", "ok", "atom", 0),
        Output("atom-from-fixnum", "42", "atom", 1),
        Output("atom-from-big-integer", "9223372036854775808", "atom", 1),
        Output("atom-from-float", "1.5", "atom", 1),
        Output("atom-from-rational", "1r2", "atom", 1),
        Output("atom-from-normalized-rational", "2r1", "atom", 1),
        Output("atom-from-compound", "foo(bar)", "atom", 1),
        Output("atom-from-list", "[a,b]", "atom", 1),
        Output("atom-from-improper-list", "[a|b]", "atom", 1),
        Output("integer-from-atom", "ok", "integer", 1),
        Output("integer-from-fixnum", "42", "integer", 0),
        Output("integer-from-negative-fixnum", "-42", "integer", 0),
        Output("integer-from-big-integer", "9223372036854775808", "integer", 1),
        Output("integer-from-widened-integer", "576460752303423488", "integer", 1),
        Output("integer-from-float", "1.5", "integer", 1),
        Output("integer-from-rational", "1r2", "integer", 1),
        Output("integer-from-normalized-rational", "2r1", "integer", 0),
        Output("integer-from-compound", "foo(bar)", "integer", 1),
        Output("integer-from-list", "[a,b]", "integer", 1),
        Output("integer-from-improper-list", "[a|b]", "integer", 1),
        Output("float-from-atom", "ok", "float", 1),
        Output("float-from-fixnum", "42", "float", 0),
        Output("float-from-big-integer", "9223372036854775808", "float", 0),
        Output("float-from-float", "1.5", "float", 0),
        Output("float-from-rational", "1r2", "float", 0),
        Output("float-from-normalized-rational", "2r1", "float", 0),
        Output("float-from-compound", "foo(bar)", "float", 1),
        Output("float-from-list", "[a,b]", "float", 1),
        Output("float-from-improper-list", "[a|b]", "float", 1),
        Output("term-from-atom", "ok", "term", 0),
        Output("term-from-fixnum", "42", "term", 0),
        Output("term-from-big-integer", "9223372036854775808", "term", 0),
        Output("term-from-float", "1.5", "term", 0),
        Output("term-from-rational", "1r2", "term", 0),
        Output("term-from-normalized-rational", "2r1", "term", 0),
        Output("term-from-compound", "foo(bar)", "term", 0),
        Output("term-from-list", "[a,b]", "term", 0),
        Output("term-from-improper-list", "[a|b]", "term", 0),
        Output("list-from-atom", "ok", "list(atom)", 1),
        Output("list-from-fixnum", "42", "list(atom)", 1),
        Output("list-from-big-integer", "9223372036854775808", "list(atom)", 1),
        Output("list-from-float", "1.5", "list(atom)", 1),
        Output("list-from-rational", "1r2", "list(atom)", 1),
        Output("list-from-normalized-rational", "2r1", "list(atom)", 1),
        Output("list-from-compound", "foo(bar)", "list(atom)", 1),
        Output("list-from-empty-list", "[]", "list(atom)", 0),
        Output("list-from-proper-list", "[a,b]", "list(atom)", 0),
        Output("list-from-improper-list", "[a|b]", "list(atom)", 1),
        Output("list-from-ground-improper-tail", "[a|1]", "list(atom)", 1),
        Output("list-from-wrong-element", "[a,1]", "list(atom)", 1),
        Output("list-from-big-integer-element", "[9223372036854775808]", "list(integer)", 1),
        Output("list-from-numeric-elements", "[1,1.5,1r2]", "list(float)", 0),
        Output("list-from-ground-compound-elements", "[foo(bar),1,a]", "list(term)", 0),
        Output("nested-list-consistent", "[[a],[b]]", "list(list(atom))", 0),
        Output("nested-list-element-violation", "[[a],[1]]", "list(list(atom))", 1),
        Output("nested-improper-list-violation", "[[a|b]]", "list(list(atom))", 1),
        Output("det-first-rule-violation", "wrong", "integer", 1, determinism: "det"),
        Output("semidet-first-rule-violation", "wrong", "integer", 1, determinism: "semidet"),
        Output("multi-first-rule-violation", "wrong", "integer", 1, determinism: "multi"),
        Output("nondet-first-rule-violation", "wrong", "integer", 1, determinism: "nondet"),
        Output("body-true-succeeds", "wrong", "integer", 1, body: "true"),
        Output("body-equal-atoms-succeed", "wrong", "integer", 1, body: "a = a"),
        Output("body-different-atoms-fail", "wrong", "integer", 0, body: "a = b"),
        Output("body-equal-compounds-succeed", "wrong", "integer", 1, body: "foo(a) = foo(a)"),
        Output("body-different-compounds-fail", "wrong", "integer", 0, body: "foo(a) = foo(b)"),
        Output("body-equal-lists-succeed", "wrong", "integer", 1, body: "[a,b] = [a,b]"),
        Output("body-different-lists-fail", "wrong", "integer", 0, body: "[a,b] = [a,c]"),
        Output("body-equal-improper-lists-succeed", "wrong", "integer", 1, body: "[a|b] = [a|b]"),
        Output("body-different-improper-lists-fail", "wrong", "integer", 0, body: "[a|b] = [a|c]"),
        Output("body-integer-unifies-with-normalized-rational", "wrong", "integer", 1, body: "1 = 1r1"),
        Output("body-integer-does-not-unify-with-float", "wrong", "integer", 0, body: "1 = 1.0"),
        Output("body-rational-does-not-unify-with-float", "wrong", "integer", 0, body: "1r1 = 1.0"),
        Output("body-equal-big-integers-succeed", "wrong", "integer", 1, body: "9223372036854775808 = 9223372036854775808"),
        Output("body-different-big-integers-fail", "wrong", "integer", 0, body: "9223372036854775808 = 9223372036854775809"),
        Output("body-conjunction-succeeds", "wrong", "integer", 1, body: "true, a = a"),
        Output("body-parenthesized-conjunction-succeeds", "wrong", "integer", 1, body: "(a = a, true)"),
        Output("body-nested-conjunction-succeeds", "wrong", "integer", 1, body: "true, (a = a, [a] = [a])"),
        Output("body-right-conjunct-fails", "wrong", "integer", 0, body: "true, a = b"),
        Output("body-left-conjunct-fails", "wrong", "integer", 0, body: "a = b, true"),
        Output("body-later-conjunct-fails", "wrong", "integer", 0, body: "a = a, true, [a] = [b]"),
        Invocation("det-earlier-success-hides-later-rule", "p(1, 1) :- true. p(1, wrong) :- true.", "integer", 0),
        Invocation("det-earlier-failure-reaches-later-rule", "p(1, 1) :- a = b. p(1, wrong) :- true.", "integer", 1),
        Invocation("det-different-input-reaches-later-rule", "p(1, 1) :- true. p(2, wrong) :- true.", "integer", 1),
        Invocation("det-earlier-fact-hides-later-rule", "p(1, 1). p(1, wrong) :- true.", "integer", 0),
        Invocation("det-different-fact-input-reaches-later-rule", "p(1, 1). p(2, wrong) :- true.", "integer", 1),
        Invocation("det-nearer-success-hides-third-rule", "p(1, 1) :- true. p(2, 2) :- true. p(2, wrong) :- true.", "integer", 0),
        Invocation(
            "det-nearer-failure-reaches-third-rule",
            "p(1, 1) :- true. p(2, 2) :- a = b. p(2, wrong) :- true.",
            "integer",
            1
        ),
        Invocation(
            "det-distinct-third-rule-is-reachable",
            "p(1, 1) :- true. p(2, 2) :- true. p(3, wrong) :- true.",
            "integer",
            1
        ),
        Invocation("det-earlier-incompatible-output-still-hides", "p(1, wrong) :- true. p(1, also_wrong) :- true.", "integer", 1),
        Invocation(
            "semidet-earlier-success-hides-later-rule",
            "p(1, 1) :- true. p(1, wrong) :- true.",
            "integer",
            0,
            determinism: "semidet"
        ),
        Invocation(
            "semidet-earlier-failure-reaches-later-rule",
            "p(1, 1) :- a = b. p(1, wrong) :- true.",
            "integer",
            1,
            determinism: "semidet"
        ),
        Invocation(
            "semidet-different-input-reaches-later-rule",
            "p(1, 1) :- true. p(2, wrong) :- true.",
            "integer",
            1,
            determinism: "semidet"
        ),
        Invocation(
            "semidet-earlier-fact-hides-later-rule",
            "p(1, 1). p(1, wrong) :- true.",
            "integer",
            0,
            determinism: "semidet"
        ),
        Invocation(
            "semidet-different-fact-input-reaches-later-rule",
            "p(1, 1). p(2, wrong) :- true.",
            "integer",
            1,
            determinism: "semidet"
        ),
        Invocation(
            "multi-equal-input-reaches-later-rule",
            "p(1, 1) :- true. p(1, wrong) :- true.",
            "integer",
            1,
            determinism: "multi"
        ),
        Invocation(
            "multi-different-input-reaches-later-rule",
            "p(1, 1) :- true. p(2, wrong) :- true.",
            "integer",
            1,
            determinism: "multi"
        ),
        Invocation("multi-earlier-fact-reaches-later-rule", "p(1, 1). p(1, wrong) :- true.", "integer", 1, determinism: "multi"),
        Invocation(
            "multi-failing-later-rule-is-unreachable",
            "p(1, 1) :- true. p(1, wrong) :- a = b.",
            "integer",
            0,
            determinism: "multi"
        ),
        Invocation(
            "nondet-equal-input-reaches-later-rule",
            "p(1, 1) :- true. p(1, wrong) :- true.",
            "integer",
            1,
            determinism: "nondet"
        ),
        Invocation(
            "nondet-different-input-reaches-later-rule",
            "p(1, 1) :- true. p(2, wrong) :- true.",
            "integer",
            1,
            determinism: "nondet"
        ),
        Invocation(
            "nondet-earlier-fact-reaches-later-rule",
            "p(1, 1). p(1, wrong) :- true.",
            "integer",
            1,
            determinism: "nondet"
        ),
        Invocation(
            "nondet-failing-later-rule-is-unreachable",
            "p(1, 1) :- true. p(1, wrong) :- a = b.",
            "integer",
            0,
            determinism: "nondet"
        ),
        Custom(
            "det-no-input-hides-later",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1) :- true. p(wrong) :- true.")],
            "p/1, det, [out(value, integer)]",
            0
        ),
        Custom(
            "semidet-no-input-hides-later",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1) :- true. p(wrong) :- true.")],
            "p/1, semidet, [out(value, integer)]",
            0
        ),
        Custom(
            "multi-no-input-reaches-later",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1) :- true. p(wrong) :- true.")],
            "p/1, multi, [out(value, integer)]",
            1
        ),
        Custom(
            "nondet-no-input-reaches-later",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1) :- true. p(wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            1
        ),
        Custom(
            "repeated-body-comparisons-use-fresh-state",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(1, wrong) :- foo(a) = foo(a). p(2, also_wrong) :- [a,b] = [a,b].")],
            "p/2, det, [in(item, integer), out(value, integer)]",
            2
        ),
        Invocation("atom-input-is-valid", "p(a, wrong) :- true.", "atom", 1),
        Invocation("integer-input-is-valid", "p(42, wrong) :- true.", "integer", 1),
        Invocation("float-input-is-valid", "p(1.5, wrong) :- true.", "float", 1),
        Invocation("term-input-is-valid", "p(foo(bar), wrong) :- true.", "term", 1),
        Invocation("list-input-is-valid", "p([a,b], wrong) :- true.", "list(atom)", 1),
        Invocation("nested-list-input-is-valid", "p([[a],[b]], wrong) :- true.", "list(list(atom))", 1),
        Invocation("widened-64-bit-input-is-valid", "p(576460752303423488, wrong) :- true.", "integer", 1),
        Invocation("integer-input-is-not-atom", "p(1, wrong) :- true.", "atom", 0),
        Invocation("float-input-is-not-integer", "p(1.0, wrong) :- true.", "integer", 0),
        Invocation("big-input-is-not-64-bit-integer", "p(9223372036854775808, wrong) :- true.", "integer", 0),
        Invocation("integer-input-has-no-float-coercion", "p(1, wrong) :- true.", "float", 0),
        Invocation("rational-input-has-no-float-coercion", "p(1r2, wrong) :- true.", "float", 0),
        Invocation("improper-list-input-is-unknown", "p([a|b], wrong) :- true.", "list(atom)", 0),
        Invocation("wrong-list-element-input-is-unknown", "p([1], wrong) :- true.", "list(atom)", 0),
        Invocation("variable-input-is-unknown", "p(X, wrong) :- true.", "atom", 0),
        Invocation("partially-ground-input-is-unknown", "p(foo(X), wrong) :- true.", "term", 0),
        Invocation("open-list-input-is-unknown", "p([a|X], wrong) :- true.", "list(atom)", 0),
        Invocation("quoted-input-is-reader-dependent", "p(\"text\", wrong) :- true.", "atom", 0),
        Output("variable-output-is-unknown", "X", "integer", 0),
        Output("compound-variable-output-is-unknown", "foo(X)", "term", 0),
        Output("open-list-output-is-unknown", "[a|X]", "list(atom)", 0),
        Output("open-list-term-output-is-unknown", "[a|X]", "term", 0),
        Output("quoted-output-is-reader-dependent", "\"text\"", "integer", 0),
        RejectedBody("variable-body-is-unknown", "X"),
        RejectedBody("left-variable-unification-is-unknown", "X = a"),
        RejectedBody("right-variable-unification-is-unknown", "a = X"),
        RejectedBody("partially-ground-unification-is-unknown", "foo(X) = foo(a)"),
        RejectedBody("call-body-is-unknown", "q"),
        RejectedBody("explicit-call-is-unknown", "call(true)"),
        RejectedBody("unknown-later-conjunct-rejects-table", "true, q"),
        RejectedBody("unknown-after-failure-rejects-table", "a = b, X = a"),
        RejectedBody("cut-is-unknown", "!"),
        RejectedBody("disjunction-is-unknown", "(true; true)"),
        RejectedBody("negation-is-unknown", "\\+ true"),
        RejectedBody("conditional-is-unknown", "(true -> true)"),
        RejectedBody("all-solutions-is-unknown", "findall(X, true, X)"),
        RejectedBody("arithmetic-equality-is-unknown", "1 =:= 1"),
        RejectedBody("arithmetic-evaluation-is-unknown", "1 is 1"),
        RejectedBody("arithmetic-comparison-is-unknown", "1 < 2"),
        RejectedBody("quoted-body-is-reader-dependent", "\"a\" = \"a\""),
        RejectedBody("quoted-body-after-failure-rejects-table", "a = b, \"a\" = \"a\""),
        Output("dynamic-target-is-unknown", "wrong", "integer", 0, prefix: ":- dynamic(p/1). "),
        Output("multifile-target-is-unknown", "wrong", "integer", 0, prefix: ":- multifile(p/1). "),
        Output(
            "initialization-effect-is-unknown-and-not-executed",
            "wrong",
            "integer",
            0,
            prefix: ":- initialization(halt(9)). "
        ),
        Output("database-effect-is-unknown", "wrong", "integer", 0, prefix: ":- assertz(q(1)). "),
        Output("source-loading-effect-is-unknown", "wrong", "integer", 0, prefix: ":- ensure_loaded('missing.pl'). "),
        Output("operator-effect-is-unknown", "wrong", "integer", 0, prefix: ":- op(500, xfy, custom). "),
        Output("flag-effect-is-unknown", "wrong", "integer", 0, prefix: ":- set_prolog_flag(double_quotes, atom). "),
        Custom(
            "missing-clauses-are-unknown",
            [Unit("corpus.pl", ":- module(m, [p/1]).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "leading-grammar-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p --> [].")],
            "p/2, det, [out(left, integer), out(right, integer)]",
            0
        ),
        Custom(
            "ordinary-then-grammar-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(ok, 1) :- true. p --> []. p(2, wrong) :- true.")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Custom(
            "grammar-then-ordinary-is-unknown",
            [Unit("corpus.pl", ":- module(m, [p/2]). p --> []. p(ok, 1) :- true. p(2, wrong) :- true.")],
            "p/2, det, [in(item, atom), out(value, integer)]",
            0
        ),
        Custom(
            "imported-only-definition-is-unknown",
            [
                Unit("a.pl", ":- module(a, [p/1]). p(wrong) :- true."),
                Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl')."),
            ],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "reexported-only-definition-is-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(wrong) :- true."), Unit("rules.pl", ":- module(m, [p/1]). :- reexport(a).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "ambiguous-export-modules-are-unknown",
            [Unit("a.pl", ":- module(a, [p/1]). p(wrong) :- true."), Unit("b.pl", ":- module(b, [p/1]). p(wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "unexported-definition-is-unknown",
            [Unit("corpus.pl", ":- module(m, []). p(wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "imported-effect-is-unknown",
            [
                Unit("a.pl", ":- module(a, [q/1]). :- initialization(halt(9)). q(1)."),
                Unit("rules.pl", ":- module(m, [p/1]). :- use_module('a.pl'). p(wrong) :- true."),
            ],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "multiple-outputs-have-independent-results",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(wrong, 42) :- true.")],
            "p/2, det, [out(first, integer), out(second, integer)]",
            1
        ),
        Custom(
            "multiple-incompatible-outputs-have-separate-findings",
            [Unit("corpus.pl", ":- module(m, [p/2]). p(wrong, also_wrong) :- true.")],
            "p/2, det, [out(first, integer), out(second, integer)]",
            2
        ),
        Custom(
            "mixed-leading-fact-and-rule-keep-existing-rule-boundaries",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(wrong). p(also_wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            1,
            expectedLeading: 1
        ),
        Custom(
            "mixed-leading-rule-does-not-enable-later-fact-analysis",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1) :- true. p(wrong).")],
            "p/1, nondet, [out(value, integer)]",
            0
        ),
        Custom(
            "mixed-compatible-fact-and-rule",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(1). p(wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            1
        ),
        Custom(
            "fact-only-table-has-no-ground-rule-findings",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(wrong). p(also_wrong).")],
            "p/1, nondet, [out(value, integer)]",
            0,
            expectedLeading: 1,
            expectedLater: 1
        ),
        Custom(
            "mixed-rules-have-independent-findings",
            [Unit("corpus.pl", ":- module(m, [p/1]). p(wrong) :- true. p(also_wrong) :- true.")],
            "p/1, nondet, [out(value, integer)]",
            2
        ),
        Output("strict-atom-output-violation", "wrong", "integer", 1, PrologLanguageMode.StrictIso),
        Output("strict-integer-output-consistent", "42", "integer", 0, PrologLanguageMode.StrictIso),
        Output("strict-list-element-violation", "[a,1]", "list(atom)", 1, PrologLanguageMode.StrictIso),
        Output("strict-successful-ground-body", "wrong", "integer", 1, PrologLanguageMode.StrictIso, body: "true, 1 = 1"),
        Output("strict-failing-ground-body", "wrong", "integer", 0, PrologLanguageMode.StrictIso, body: "1 = 1.0"),
        Invocation(
            "strict-integer-input-has-no-float-coercion",
            "p(1, wrong) :- true.",
            "float",
            0,
            PrologLanguageMode.StrictIso
        ),
    ];

    public GroundRuleContractOutputLintCorpusTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void VersionedCorpusProducesOnlyIndependentlyExpectedFindings()
    {
        Assert.Equal(1, CorpusVersion);
        Assert.Equal(Cases.Length, Cases.Select(test => test.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (CorpusCase test in Cases)
        {
            IReadOnlyList<Diagnostic> diagnostics = Analyze(test);
            AssertFindingCount(test, diagnostics, LintDiagnosticIds.GroundRuleContractOutputTypeMismatch, test.ExpectedFindings);
            AssertFindingCount(test, diagnostics, LintDiagnosticIds.ContractOutputTypeMismatch, test.ExpectedLeading);
            AssertFindingCount(test, diagnostics, LintDiagnosticIds.LaterContractOutputTypeMismatch, test.ExpectedLater);
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
                    Analyze(test).Count(diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
                )
        );
    }

    [Fact]
    public void EveryRepositoryContractIsFreeOfGroundRuleOutputViolations()
    {
        string root = RepositoryRoot();
        string[] contracts = Directory.GetFiles(root, "*.dpli", SearchOption.AllDirectories);
        Assert.NotEmpty(contracts);
        foreach (string contractPath in contracts)
        {
            string sourcePath = Path.ChangeExtension(contractPath, ".pl");
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
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch
            );
        }
    }

    [Fact]
    public void IncludedRuleFindingUsesPhysicalFileAndOutputSpan()
    {
        string mainPath = Path.Combine(_directory, "rules.pl");
        string rulePath = Path.Combine(_directory, "guarded.pl");
        const string rules = "p(wrong) :- true.\n";
        File.WriteAllText(mainPath, ":- module(m, [p/1]). :- include('guarded.pl').\n");
        File.WriteAllText(rulePath, rules);
        PrologLintSource loaded = PrologLintSourceLoader.Load(mainPath, File.ReadAllText(mainPath), new BytecodeProgram()).Source;
        ContractReadResult contract = ContractReader.Read(
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            "Corpus",
            "corpus.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        IReadOnlyList<Diagnostic> diagnostics = PrologContractLinter.Analyze(
            [loaded],
            contract.Contract!,
            "corpus.dpli",
            Enabled
        );

        Diagnostic warning = Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch
        );
        Assert.Equal(rulePath, warning.FileName);
        Assert.Equal(rules.IndexOf("wrong", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("wrong".Length, warning.Span.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CorpusCase Output(
        string name,
        string output,
        string type,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        string determinism = "nondet",
        string body = "true",
        string prefix = ""
    ) =>
        Custom(
            name,
            [Unit("corpus.pl", $":- module(m, [p/1]). {prefix}p({output}) :- {body}.")],
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

    private static CorpusCase RejectedBody(string name, string body) =>
        Custom(
            name,
            [Unit("corpus.pl", $":- module(m, [p/1]). p(wrong) :- true. p(1) :- {body}.")],
            "p/1, nondet, [out(value, integer)]",
            0
        );

    private static CorpusCase Custom(
        string name,
        CorpusSource[] sources,
        string export,
        int expectedFindings,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        int expectedLeading = 0,
        int expectedLater = 0
    ) => new(name, sources, export, expectedFindings, mode, expectedLeading, expectedLater);

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

    private static void AssertFindingCount(CorpusCase test, IReadOnlyList<Diagnostic> diagnostics, string id, int expected)
    {
        int actual = diagnostics.Count(diagnostic => diagnostic.Id == id);
        Assert.True(actual == expected, $"{test.Name}: expected {expected} {id} findings, actual {actual}.");
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
        PrologLanguageMode Mode,
        int ExpectedLeading,
        int ExpectedLater
    );

    private sealed record CorpusSource(string FileName, string Text);
}
