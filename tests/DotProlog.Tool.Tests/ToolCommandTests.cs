namespace DotProlog.Tool.Tests;

public sealed class ToolCommandTests : IDisposable
{
    [Theory]
    [InlineData("lint")]
    [InlineData("run")]
    public void DeepSourceReturnsADiagnostic(string command)
    {
        var path = Source("deep.pl", new string('(', 20_000) + "a" + new string(')', 20_000) + ".");
        (var exitCode, _, var error) = Execute(command, path);
        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL0012", error, StringComparison.Ordinal);
        Assert.Contains(path + "(1,", error, StringComparison.Ordinal);
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog.Tool.Tests", Guid.NewGuid().ToString("N"));

    public ToolCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LintReportsWarningsWithoutFailingByDefault()
    {
        var path = Source("singleton.pl", "value(X).\n");

        (var exitCode, var output, var error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{path}(1,7): warning DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsLiteralSemanticWarningsThroughTheCli()
    {
        var path = Source("format.pl", "run :- format(\"~w\", [a, b]).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3013", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsAsErrorsReturnsOne()
    {
        var path = Source("singleton.pl", "value(X).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderErrorsTakePrecedenceOverWarnings()
    {
        var path = Source("broken.pl", "value(X\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintAcceptsModesAndMultipleFiles()
    {
        var clean = Source("clean.pl", "same(X, X).\n");
        var warning = Source("warning.pl", "same(_Value, _Value).\n");

        (var exitCode, _, var error) = Execute("lint", "--mode", "modern", clean, warning);

        Assert.Equal(0, exitCode);
        Assert.Contains("DPL3002", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintResolvesNamedFilesAsOneProgram()
    {
        var main = Source("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go :- helper.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void LintReportsAnUndefinedCrossFileCall()
    {
        var main = Source("main.pl", ":- module(main, []). :- use_module('other.pl'). go :- missing.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3029", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsConflictingImportsByDefault()
    {
        var a = Source("a.pl", ":- module(a, [p/0]). p.\n");
        var b = Source("b.pl", ":- module(b, [p/0]). p.\n");
        var main = Source("conflict.pl", ":- module(m, []). :- import(a, [p/0]). :- import(b, [p/0]).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", main, a, b);
        (var enabledExit, _, var enabledError) = Execute("lint", "--warnings-as-errors", main, a, b);

        Assert.Equal(0, defaultExit);
        Assert.Contains("DPL3047", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3047", enabledError, StringComparison.Ordinal);
        Assert.Contains("earlier import from module a", enabledError, StringComparison.Ordinal);
        Assert.Contains("from module b", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalImportShadowRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var a = Source("shadow-source.pl", ":- module(a, [p/0]). p.\n");
        var main = Source("shadow-main.pl", ":- module(m, [p/0]). :- initialization(halt(9)). :- import(a, [p/0]). p.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", main, a);
        (var enabledExit, _, var enabledError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3048",
            "--enable-rule",
            "dpl3048",
            "--warnings-as-errors",
            main,
            a
        );

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3048", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Equal(1, enabledError.Split(["DPL3048"], StringSplitOptions.None).Length - 1);
        Assert.Contains("Local procedure p/0 in module m", enabledError, StringComparison.Ordinal);
        Assert.Contains("from module a", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltinShadowRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("builtin-shadow.pl", ":- initialization(halt(9)). member(_, _).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3049",
            "--enable-rule",
            "dpl3049",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3049", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Equal(1, enabledError.Split(["DPL3049"], StringSplitOptions.None).Length - 1);
        Assert.Contains("Local procedure member/2 in module user", enabledError, StringComparison.Ordinal);
        Assert.Contains("predefined predicate member/2", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsInconsistentMetaPredicateDeclarationsByDefault()
    {
        var path = Source(
            "meta-declarations.pl",
            ":- initialization(halt(9)). :- meta_predicate(p(0)). :- meta_predicate(p(1)). p(_).\n"
        );

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Contains("DPL3050", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3050", enabledError, StringComparison.Ordinal);
        Assert.Contains("declaration p(1) for user:p/1", enabledError, StringComparison.Ordinal);
        Assert.Contains("earlier template p(0)", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsMissingExportsByDefaultWithoutExecutingSource()
    {
        var a = Source("missing-export-source.pl", ":- module(a, [q/0]). q.\n");
        var main = Source(
            "missing-export-main.pl",
            ":- module(m, []). :- initialization(halt(9)). :- use_module('missing-export-source.pl', [p/0]).\n"
        );

        (var defaultExit, _, var defaultError) = Execute("lint", main, a);
        (var enabledExit, _, var enabledError) = Execute("lint", "--warnings-as-errors", main, a);

        Assert.Equal(0, defaultExit);
        Assert.Contains("DPL3051", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3051", enabledError, StringComparison.Ordinal);
        Assert.Contains("Import of p/0 into module m from module a", enabledError, StringComparison.Ordinal);
        Assert.Contains("binding is not resolved", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractOutputRuleIsDefaultAndHonorsWarningsAsErrorsWithoutExecution()
    {
        var source = Source("contract-rules.pl", ":- module(m, [p/1]). p(wrong).\n");
        var contract = Source("contract-rules.dpli", ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)]).\n");
        var canary = Source("contract-canary.pl", ":- module(canary, []). :- initialization(halt(9)).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", "--contract", contract, source, canary);
        (var enabledExit, _, var enabledError) = Execute(
            "lint",
            "--enable-rule",
            "dpl3052",
            "--contract",
            contract,
            source,
            canary
        );
        (var strictExit, _, var strictError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3052",
            "--contract",
            contract,
            "--warnings-as-errors",
            source,
            canary
        );

        Assert.Equal(0, defaultExit);
        Assert.Contains("DPL3052", defaultError, StringComparison.Ordinal);
        Assert.Equal(0, enabledExit);
        Assert.Contains("DPL3052", enabledError, StringComparison.Ordinal);
        Assert.Equal(defaultError, enabledError);
        Assert.Equal(1, strictExit);
        Assert.Contains("DPL3052", strictError, StringComparison.Ordinal);
        Assert.Contains("Output value of m:p/1", strictError, StringComparison.Ordinal);
    }

    [Fact]
    public void LaterContractOutputRuleIsDefaultAndHonorsWarningsAsErrorsWithoutExecution()
    {
        var source = Source("later-contract-rules.pl", ":- module(m, [p/2]). p(1, 1). p(2, wrong).\n");
        var contract = Source(
            "later-contract-rules.dpli",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, integer), out(value, integer)]).\n"
        );
        var canary = Source("later-contract-canary.pl", ":- module(canary, []). :- initialization(halt(9)).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", "--contract", contract, source, canary);
        (var enabledExit, _, var enabledError) = Execute(
            "lint",
            "--enable-rule",
            "dpl3053",
            "--contract",
            contract,
            source,
            canary
        );
        (var repeatedExit, _, var repeatedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3053",
            "--enable-rule",
            "DPL3053",
            "--contract",
            contract,
            source,
            canary
        );
        (var strictExit, _, var strictError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3053",
            "--contract",
            contract,
            "--warnings-as-errors",
            source,
            canary
        );

        Assert.Equal(0, defaultExit);
        Assert.Contains("DPL3053", defaultError, StringComparison.Ordinal);
        Assert.Equal(0, enabledExit);
        Assert.Contains("DPL3053", enabledError, StringComparison.Ordinal);
        Assert.Equal(defaultError, enabledError);
        Assert.Contains("later fact output", enabledError, StringComparison.Ordinal);
        Assert.Equal(0, repeatedExit);
        Assert.Equal(enabledError, repeatedError);
        Assert.Equal(1, strictExit);
        Assert.Contains("DPL3053", strictError, StringComparison.Ordinal);
    }

    [Fact]
    public void GroundRuleContractOutputRuleIsOptInAndHonorsWarningsAsErrorsWithoutExecution()
    {
        var source = Source("ground-rule-contract-rules.pl", ":- module(m, [p/2]). p(1, 1) :- a = b. p(1, wrong) :- true.\n");
        var contract = Source(
            "ground-rule-contract-rules.dpli",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, integer), out(value, integer)]).\n"
        );
        var canary = Source("ground-rule-contract-canary.pl", ":- module(canary, []). :- initialization(halt(9)).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", "--contract", contract, source, canary);
        (var enabledExit, _, var enabledError) = Execute(
            "lint",
            "--enable-rule",
            "dpl3054",
            "--contract",
            contract,
            source,
            canary
        );
        (var repeatedExit, _, var repeatedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3054",
            "--enable-rule",
            "dpl3054",
            "--contract",
            contract,
            source,
            canary
        );
        (var strictExit, _, var strictError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3054",
            "--contract",
            contract,
            "--warnings-as-errors",
            source,
            canary
        );

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3054", defaultError, StringComparison.Ordinal);
        Assert.Equal(0, enabledExit);
        Assert.Contains("DPL3054", enabledError, StringComparison.Ordinal);
        Assert.Contains("ground rule output", enabledError, StringComparison.Ordinal);
        Assert.Equal(0, repeatedExit);
        Assert.Equal(enabledError, repeatedError);
        Assert.Equal(1, strictExit);
        Assert.Contains("DPL3054", strictError, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractOptionRequiresExactlyOneMatchingSourceBaseName()
    {
        var source = Source("pairing-rules.pl", ":- module(m, []). \n");
        var contract = Source("other.dpli", ":- clr_module('M').\n");

        (var exitCode, _, var error) = Execute("lint", "--contract", contract, source);
        (var missingExit, _, var missingError) = Execute("lint", "--contract");

        Assert.Equal(64, exitCode);
        Assert.Contains("same base name as exactly one", error, StringComparison.Ordinal);
        Assert.Equal(64, missingExit);
        Assert.Contains("missing contract file", missingError, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractReaderErrorsReturnCompileError()
    {
        var source = Source("invalid-contract.pl", ":- module(m, [p/1]). p(wrong).\n");
        var contract = Source(
            "invalid-contract.dpli",
            ":- clr_module('M'). :- clr_export(p/1, unknown, [out(value, integer)]).\n"
        );

        (var exitCode, _, var error) = Execute("lint", "--enable-rule", "DPL3052", "--contract", contract, source);

        Assert.Equal(65, exitCode);
        Assert.Contains("error DPL2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledContractRuleWithoutContractHasNothingToReport()
    {
        var source = Source("no-contract.pl", ":- module(m, [p/1]). p(wrong).\n");

        (var exitCode, _, var error) = Execute("lint", "--enable-rule", "DPL3052", source);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("DPL3052", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAGroundCallWithNoMatchingClauseHead()
    {
        var path = Source("trivial.pl", "p(a). go :- p(b).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3033", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnreferencedPredicate()
    {
        var path = Source("reachability.pl", ":- module(m, [go/0]).\ngo.\nunused.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3034", error, StringComparison.Ordinal);
        Assert.Contains("m:unused/0", error, StringComparison.Ordinal);
        Assert.Contains("may be unreferenced", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnreferencedNonterminal()
    {
        var path = Source("grammar.pl", ":- module(m, []).\ndead_grammar --> [a].\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3036", error, StringComparison.Ordinal);
        Assert.Contains("m:dead_grammar//0", error, StringComparison.Ordinal);
        Assert.Contains("may be unreferenced", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAPossiblyUnusedImport()
    {
        var main = Source("main.pl", ":- module(main, [go/0]). :- use_module('other.pl', [helper/0]). go.\n");
        var other = Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main, other);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3035", error, StringComparison.Ordinal);
        Assert.Contains("helper/0", error, StringComparison.Ordinal);
        Assert.Contains("may be unused", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsAnUnresolvedInclude()
    {
        var main = Source("main.pl", ":- include('missing.pl').\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", main);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3032", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintDoesNotExecuteDirectives()
    {
        var path = Source("directive.pl", ":- initialization(halt(9)).\nsafe.\n");

        (var exitCode, var output, var error) = Execute("lint", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public void SemanticProfileDoesNotImposeLayoutRules()
    {
        var path = Source("compact.pl", "pair(a,b).\n");

        (var exitCode, _, var error) = Execute("lint", "--warnings-as-errors", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void CovingtonProfileReportsLayoutWarnings()
    {
        var path = Source("compact.pl", "pair(a,b).\n");

        (var exitCode, _, var error) = Execute("lint", "--profile", "covington", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains($"{path}(1,7): warning DPL3007", error, StringComparison.Ordinal);
    }

    [Fact]
    public void IndividualThresholdEnablesItsLayoutCheck()
    {
        var path = Source("wide.pl", "long_name.\n");

        (var exitCode, _, var error) = Execute("lint", "--max-line-length", "5", "--warnings-as-errors", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3005", error, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSteadfastRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("max.pl", "max(X, Y, X) :- X >= Y, !.\nmax(_, Y, Y).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "DPL3037", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3037", enabledError, StringComparison.Ordinal);
        Assert.Contains("max/3", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatingEnableRuleDoesNotDuplicateDiagnostics()
    {
        var path = Source("max.pl", "max(X, Y, X) :- X >= Y, !.\n");

        (var exitCode, _, var error) = Execute(
            "lint",
            "--enable-rule",
            "DPL3037",
            "--enable-rule",
            "dpl3037",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(1, exitCode);
        Assert.Equal(1, error.Split("DPL3037", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void NonTailRecursionRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("recursive.pl", "p(N) :- N > 0, p(N), writeln(N).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3038", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3038", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/1", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void MultifileCutRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("multifile.pl", ":- multifile(p/0).\np :- !.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3039", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3039", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatLoopRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("repeat.pl", "p :- repeat, fail.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3040", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3040", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCutRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("final-cut.pl", "p :- true, !.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3041", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3041", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void CatchAllRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("catch-all.pl", "p :- catch(true, _, true).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3042", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3042", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3042", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingElseRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("missing-else.pl", "p :- (true -> true).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3043", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3043", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3043", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void NamingRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("naming.pl", "badName.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3044", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3044", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3044", enabledError, StringComparison.Ordinal);
        Assert.Contains("badName/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveArityRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("arity.pl", "p(A, B, C, D, E).\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3045", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3045", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3045", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/5", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveArityLimitCanBeOverriddenAndIsInertWithoutRule()
    {
        var path = Source("arity-limit.pl", "p(A, B, C, D, E).\n");

        (var inertExit, _, var inertError) = Execute("lint", "--max-predicate-arity", "1", path);
        (var acceptedExit, _, var acceptedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3045",
            "--max-predicate-arity",
            "5",
            path
        );
        (var repeatedExit, _, var repeatedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3045",
            "--max-predicate-arity",
            "5",
            "--max-predicate-arity",
            "4",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(0, inertExit);
        Assert.DoesNotContain("DPL3045", inertError, StringComparison.Ordinal);
        Assert.Equal(0, acceptedExit);
        Assert.DoesNotContain("DPL3045", acceptedError, StringComparison.Ordinal);
        Assert.Equal(1, repeatedExit);
        Assert.Contains("maximum of 4", repeatedError, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveSubgoalCountRuleIsDisabledUnlessExplicitlyEnabled()
    {
        var path = Source("subgoals.pl", "p :- a,b,c,d,e,f,g,h,i.\n");

        (var defaultExit, _, var defaultError) = Execute("lint", path);
        (var enabledExit, _, var enabledError) = Execute("lint", "--enable-rule", "dpl3046", "--warnings-as-errors", path);

        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain("DPL3046", defaultError, StringComparison.Ordinal);
        Assert.Equal(1, enabledExit);
        Assert.Contains("DPL3046", enabledError, StringComparison.Ordinal);
        Assert.Contains("p/0", enabledError, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveSubgoalLimitCanBeOverriddenAndIsInertWithoutRule()
    {
        var path = Source("subgoal-limit.pl", "p :- a,b,c,d,e,f,g,h,i.\n");

        (var inertExit, _, var inertError) = Execute("lint", "--max-clause-subgoals", "1", path);
        (var acceptedExit, _, var acceptedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3046",
            "--max-clause-subgoals",
            "9",
            path
        );
        (var repeatedExit, _, var repeatedError) = Execute(
            "lint",
            "--enable-rule",
            "DPL3046",
            "--max-clause-subgoals",
            "9",
            "--max-clause-subgoals",
            "8",
            "--warnings-as-errors",
            path
        );

        Assert.Equal(0, inertExit);
        Assert.DoesNotContain("DPL3046", inertError, StringComparison.Ordinal);
        Assert.Equal(0, acceptedExit);
        Assert.DoesNotContain("DPL3046", acceptedError, StringComparison.Ordinal);
        Assert.Equal(1, repeatedExit);
        Assert.Contains("maximum of 8", repeatedError, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleOptInRulesCanBeEnabledTogether()
    {
        var imported = Source("heuristics-import.pl", ":- module(shadow_source, [shadowed/0]). shadowed.\n");
        var path = Source(
            "heuristics.pl",
            ":- module(heuristics, []). :- import(shadow_source, [shadowed/0]).\nshadowed.\nmember(_, _).\n:- multifile(p/0).\nmax(X, Y, X) :- X >= Y, !.\np :- !.\np(N) :- N > 0, p(N), writeln(N).\nq :- repeat.\nr :- true, !.\ns :- catch(true, _, true).\nt :- (true -> true).\nbadName.\nu(A, B, C, D, E).\nv :- a,b,c,d,e,f,g,h,i.\n"
        );

        (var exitCode, _, var error) = Execute(
            "lint",
            "--enable-rule",
            "DPL3037",
            "--enable-rule",
            "DPL3038",
            "--enable-rule",
            "DPL3039",
            "--enable-rule",
            "DPL3040",
            "--enable-rule",
            "DPL3041",
            "--enable-rule",
            "DPL3042",
            "--enable-rule",
            "DPL3043",
            "--enable-rule",
            "DPL3044",
            "--enable-rule",
            "DPL3045",
            "--enable-rule",
            "DPL3046",
            "--enable-rule",
            "DPL3048",
            "--enable-rule",
            "DPL3049",
            "--warnings-as-errors",
            path,
            imported
        );

        Assert.Equal(1, exitCode);
        Assert.Contains("DPL3037", error, StringComparison.Ordinal);
        Assert.Contains("DPL3038", error, StringComparison.Ordinal);
        Assert.Contains("DPL3039", error, StringComparison.Ordinal);
        Assert.Contains("DPL3040", error, StringComparison.Ordinal);
        Assert.Contains("DPL3041", error, StringComparison.Ordinal);
        Assert.Contains("DPL3042", error, StringComparison.Ordinal);
        Assert.Contains("DPL3043", error, StringComparison.Ordinal);
        Assert.Contains("DPL3044", error, StringComparison.Ordinal);
        Assert.Contains("DPL3045", error, StringComparison.Ordinal);
        Assert.Contains("DPL3046", error, StringComparison.Ordinal);
        Assert.Contains("DPL3048", error, StringComparison.Ordinal);
        Assert.Contains("DPL3049", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "unknown", "unknown lint profile")]
    [InlineData("--enable-rule", "DPL3001", "unsupported lint rule")]
    [InlineData("--indent-size", "0", "invalid value")]
    [InlineData("--max-line-length", "word", "invalid value")]
    [InlineData("--max-clause-lines", "-1", "invalid value")]
    [InlineData("--max-predicate-arity", "0", "invalid value")]
    [InlineData("--max-predicate-arity", "-1", "invalid value")]
    [InlineData("--max-predicate-arity", "word", "invalid value")]
    [InlineData("--max-predicate-arity", "999999999999999999999999", "invalid value")]
    [InlineData("--max-clause-subgoals", "0", "invalid value")]
    [InlineData("--max-clause-subgoals", "-1", "invalid value")]
    [InlineData("--max-clause-subgoals", "word", "invalid value")]
    [InlineData("--max-clause-subgoals", "999999999999999999999999", "invalid value")]
    public void InvalidLintOptionValuesAreUsageErrors(string option, string value, string message)
    {
        var path = Source("clean.pl", "clean.\n");

        (var exitCode, _, var error) = Execute("lint", option, value, path);

        Assert.Equal(64, exitCode);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "missing lint profile")]
    [InlineData("--enable-rule", "missing diagnostic identifier")]
    [InlineData("--indent-size", "missing positive integer")]
    [InlineData("--max-line-length", "missing positive integer")]
    [InlineData("--max-clause-lines", "missing positive integer")]
    [InlineData("--max-predicate-arity", "missing positive integer")]
    [InlineData("--max-clause-subgoals", "missing positive integer")]
    public void MissingLintOptionValuesAreUsageErrors(string option, string message)
    {
        (var exitCode, _, var error) = Execute("lint", option);

        Assert.Equal(64, exitCode);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWritesProgramOutputToTheProvidedStream()
    {
        var path = Source("run.pl", ":- initialization(writeln(ok)).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunReportsAnErrorADirectiveRaisesWhileLoading()
    {
        var path = Source("directive-error.pl", ":- nowhere(1).\n:- initialization(writeln(never)).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(70, exitCode);
        Assert.Empty(output);
        Assert.Equal("error: existence_error(procedure, nowhere/1)\n", error.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("main :- writeln(hello).", 0, "first\nhello\n", "")]
    [InlineData("main :- fail.", 1, "first\n", "ERROR: main: false")]
    [InlineData("main :- X is foo + 1, writeln(X).", 2, "first\n", "ERROR: Arithmetic: `foo/0' is not a function")]
    [InlineData("main :- halt(3).", 3, "first\n", "")]
    public void RunEndsWithTheInitializationMainGoal(string main, int expectedExit, string expectedOutput, string expectedError)
    {
        var path = Source("main.pl", $":- initialization(main, main).\n:- initialization(writeln(first)).\n{main}\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(expectedExit, exitCode);
        Assert.Equal(expectedOutput, output.ReplaceLineEndings("\n"));
        Assert.Equal(expectedError, error.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void RunRunsTheGoalsOfAFileConsultedAtRunTime()
    {
        var inner = Source("inner.pl", ":- writeln(inner_directive).\n:- initialization(writeln(inner_init)).\n");
        var outer = Source(
            "outer.pl",
            $":- initialization(main).\n:- initialization(writeln(second)).\nmain :- consult('{inner.Replace('\\', '/')}'), writeln(done).\n"
        );

        (var exitCode, var output, var error) = Execute("run", outer);

        Assert.Equal(0, exitCode);
        Assert.Equal("done\ninner_directive\ninner_init\nsecond\n", output.ReplaceLineEndings("\n"));
        Assert.Empty(error);
    }

    [Fact]
    public void MissingLintInputIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("lint");

        Assert.Equal(64, exitCode);
        Assert.Contains("Usage: dotnet prolog lint", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLintModeIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("lint", "--mode");

        Assert.Equal(64, exitCode);
        Assert.Contains("missing language mode", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunHonorsAFlagOverride()
    {
        var path = Source("flag.pl", ":- initialization((current_prolog_flag(double_quotes, atom), writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", "--flag", "double_quotes=atom", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunRejectsAnInvalidFlagOverride()
    {
        var path = Source("flag.pl", "value.\n");

        (var exitCode, _, var error) = Execute("run", "--flag", "double_quotes=strings", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("invalid flag override", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RunRejectsARepeatedFlagOverride()
    {
        var path = Source("flag.pl", "value.\n");

        (var exitCode, _, var error) = Execute("run", "--flag", "double_quotes=atom", "--flag", "double_quotes=chars", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRunFlagValueIsAUsageError()
    {
        (var exitCode, _, var error) = Execute("run", "--flag");

        Assert.Equal(64, exitCode);
        Assert.Contains("missing flag override", error, StringComparison.Ordinal);
    }

    [Fact]
    public void LintAcceptsAFlagOverride()
    {
        var path = Source("clean.pl", "same(X, X).\n");

        (var exitCode, _, var error) = Execute("lint", "--flag", "double_quotes=codes", path);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    [Fact]
    public void LintJudgesDoubleQuotedTextUnderTheSelectedReading()
    {
        var path = Source("convert.pl", "digits(N) :- number_codes(N, \"42\").\n");

        (var charsExit, _, var charsError) = Execute("lint", "--warnings-as-errors", path);
        (var codesExit, _, var codesError) = Execute("lint", "--warnings-as-errors", "--flag", "double_quotes=codes", path);
        (var strictExit, _, var strictError) = Execute("lint", "--warnings-as-errors", "--mode", "strict-iso", path);

        Assert.Equal(1, charsExit);
        Assert.Contains($"{path}(1,30): warning DPL3011", charsError, StringComparison.Ordinal);
        Assert.Equal(0, codesExit);
        Assert.Empty(codesError);
        Assert.Equal(0, strictExit);
        Assert.Empty(strictError);
    }

    [Fact]
    public void RunDefaultsToModernMode()
    {
        var path = Source("default.pl", ":- initialization((\"ab\" == [a,b], writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunReadsCodeListsUnderACodesOverride()
    {
        var path = Source("codes.pl", ":- initialization((\"ab\" == [97,98], writeln(ok))).\n");

        (var exitCode, var output, var error) = Execute("run", "--flag", "double_quotes=codes", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void RunRejectsTheRemovedExtendedMode()
    {
        var path = Source("clean.pl", "same(X, X).\n");

        (var exitCode, _, var error) = Execute("run", "--mode", "extended", path);

        Assert.Equal(64, exitCode);
        Assert.Contains("unknown language mode: extended", error, StringComparison.Ordinal);
        Assert.Contains("modern|strict-iso", error, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Source(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static (int ExitCode, string Output, string Error) Execute(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = Program.Execute(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }
}
