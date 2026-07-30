using System.Text.Json;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace Integration.Tests;

/// <summary>
/// Pins and inventories the independently maintained Logtalk ISO Prolog tests and executes the
/// subset whose Logtalk wrapper needs no support clauses.
/// </summary>
public sealed class LogtalkConformanceTests
{
    private const string OptInVariable = "DOTPROLOG_RUN_EXTERNAL_CONFORMANCE_TESTS";
    private const string CaseVariable = "DOTPROLOG_LOGTALK_CASE_ID";
    private const string ReportVariable = "DOTPROLOG_LOGTALK_REPORT_PATH";
    private const string Repository = "https://github.com/LogtalkDotOrg/logtalk3.git";
    private const string Tag = "lgt31010stable";
    private const string Commit = "11dfd24eb6673250be996012489e65c0f9370a7c";
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    [Fact]
    public void AdapterPreservesDeclarationsExpectationsAndCharacterCodeSyntax()
    {
        const string source = """
            % A dot and comma inside terms are not declaration boundaries.
            :- dynamic(state/1).
            :- discontiguous(helper/1).

            helper(X) :-
                {atom(X)}.

            cleanup :-
                ^^clean_text_input.

            test(iso_fixture_01, true(X == 1.0)) :-
                {X = 1.0},
                {Y = pair(a, b)}.

            test(iso_fixture_02, error(type_error(character,0'.))) :-
                {char_code(0'., _)}.

            - test(iso_fixture_03, false, [note('upstream disabled')]) :-
                {fail}.

            quick_check(iso_fixture_04, round_trip(+integer)).
            """;

        IReadOnlyList<LogtalkTestDeclaration> declarations = LogtalkTestAdapter.ReadDeclarations(source, "fixture.lgt");

        Assert.Collection(
            declarations,
            first =>
            {
                Assert.Equal("iso_fixture_01", first.Id);
                Assert.Equal("true(X == 1.0)", first.Outcome);
                Assert.Equal(
                    """
                    {X = 1.0},
                        {Y = pair(a, b)}
                    """,
                    first.Body
                );
                Assert.False(first.Disabled);
            },
            second =>
            {
                Assert.Equal("error(type_error(character,0'.))", second.Outcome);
                Assert.Equal("{char_code(0'., _)}", second.Body);
                Assert.False(second.Disabled);
            },
            third =>
            {
                Assert.Equal("false", third.Outcome);
                Assert.Equal("[note('upstream disabled')]", third.Options);
                Assert.True(third.Disabled);
            },
            fourth =>
            {
                Assert.Equal("iso_fixture_04", fourth.Id);
                Assert.Equal("quick_check(round_trip(+integer))", fourth.Outcome);
                Assert.Equal("quick_check", fourth.OutcomeKind);
                Assert.Null(fourth.Body);
                Assert.False(fourth.Disabled);
            }
        );

        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(declarations[0], out string firstGoal));
        Assert.Equal("(X = 1.0), (Y = pair(a, b))", firstGoal);
        Assert.True(LogtalkTestAdapter.TryReadSupportProgram(source, out string supportProgram));
        Assert.Equal(
            """
            :- dynamic(state/1).
            helper(X) :-
                (atom(X)).
            """,
            supportProgram
        );
        Assert.Equal(
            "((abs((X) - (3.1415927)) < 0.0000000001) -> true ; (abs((X) - (3.1415927)) < (0.00001 * max(abs(X), abs(3.1415927)))))",
            LogtalkTestAdapter.TranslateAssertion("X =~= 3.1415927")
        );
        Assert.Equal("(current_predicate(foo/1))", LogtalkTestAdapter.TranslateAssertion("{current_predicate(foo/1)}"));

        const string conditionalSource = """
            :- if(catch({1^true}, _, fail)).
                test(iso_conditional, true) :-
                    {true}.
            :- else.
                test(iso_conditional, false) :-
                    {fail}.
            :- endif.
            """;
        IReadOnlyList<LogtalkTestDeclaration> conditional = LogtalkTestAdapter.ReadDeclarations(
            conditionalSource,
            "conditional.lgt"
        );
        Assert.Equal("catch({1^true}, _, fail)", conditional[0].ConditionalGoal);
        Assert.Equal("\\+ (catch({1^true}, _, fail))", conditional[1].ConditionalGoal);
        Assert.Equal(
            "catch((1^true), _, fail)",
            LogtalkTestAdapter.TranslateConditionalGoal(conditional[0].ConditionalGoal!)
        );
        Assert.Equal(
            "[error((type_error(callable, 1)), _), error((type_error(callable, ':'(user, 1))), _)]",
            LogtalkTestAdapter.TranslateErrorAlternatives(
                "[type_error(callable, 1), type_error(callable, ':'(user, 1))]"
            )
        );

        const string findallSource = """
            test(iso_findall_escape, variant(L, [1, 2])) :-
                findall(X, {between(1, 2, X)}, L).
            """;
        LogtalkTestDeclaration findall = Assert.Single(
            LogtalkTestAdapter.ReadDeclarations(findallSource, "findall.lgt")
        );
        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(findall, out string findallGoal));
        Assert.Equal("findall(X, ((between(1, 2, X))), L)", findallGoal);

        var adapterEngine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        var errors = new LogtalkTestDeclaration(
            "fixture.lgt",
            "iso_errors",
            "errors([type_error(atom,1), type_error(integer,1)])",
            null,
            "{throw(error(type_error(integer,1),context))}",
            false,
            null
        );
        Assert.True(Execute(errors, adapterEngine, out string errorsFailure), errorsFailure);

        var ball = new LogtalkTestDeclaration(
            "fixture.lgt",
            "iso_ball",
            "ball(bla)",
            null,
            "{throw(bla)}",
            false,
            null
        );
        Assert.True(Execute(ball, adapterEngine, out string ballFailure), ballFailure);
    }

    [Fact]
    public async Task PinnedIsoCorpusIsCompleteAndDirectCasesExecute()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the pinned independent conformance suite."
        );

        string checkout = Path.Combine(Path.GetTempPath(), $"dotprolog-logtalk-{Environment.ProcessId}");
        Directory.CreateDirectory(checkout);

        try
        {
            (int cloneExit, string cloneLog) = await ChildProcess.RunAsync(
                "git",
                ["clone", "--depth", "1", "--branch", Tag, "--filter=blob:none", "--sparse", Repository, checkout],
                RepositoryLayout.Root
            );
            Assert.True(cloneExit == 0, $"Could not clone the pinned Logtalk suite:\n{cloneLog}");

            (int sparseExit, string sparseLog) = await ChildProcess.RunAsync(
                "git",
                ["-C", checkout, "sparse-checkout", "set", "tests/prolog"],
                RepositoryLayout.Root
            );
            Assert.True(sparseExit == 0, $"Could not select the Logtalk Prolog tests:\n{sparseLog}");

            (int revisionExit, string revisionLog) = await ChildProcess.RunAsync(
                "git",
                ["-C", checkout, "rev-parse", "HEAD"],
                RepositoryLayout.Root
            );
            Assert.True(revisionExit == 0, $"Could not read the Logtalk revision:\n{revisionLog}");
            Assert.Equal(Commit, revisionLog.Trim());

            string testsRoot = Path.Combine(checkout, "tests", "prolog");
            string[] files = Directory.GetFiles(testsRoot, "tests.lgt", SearchOption.AllDirectories);
            Assert.Equal(192, files.Length);

            var sourceByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            LogtalkTestDeclaration[] declarations =
            [
                .. files.SelectMany(file =>
                {
                    string relativePath = Path.GetRelativePath(testsRoot, file).Replace('\\', '/');
                    string source = File.ReadAllText(file);
                    sourceByPath.Add(relativePath, source);
                    return LogtalkTestAdapter.ReadDeclarations(source, relativePath);
                }),
            ];

            Assert.Equal(788, declarations.Length);
            Assert.Equal(759, declarations.Count(test => !test.Disabled));
            Assert.Equal(29, declarations.Count(test => test.Disabled));

            Dictionary<string, int> outcomeKinds = declarations
                .Where(test => !test.Disabled)
                .GroupBy(test => test.OutcomeKind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            Assert.Equal(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["ball"] = 1,
                    ["deterministic"] = 1,
                    ["error"] = 154,
                    ["errors"] = 17,
                    ["exists"] = 41,
                    ["fail"] = 3,
                    ["false"] = 73,
                    ["quick_check"] = 6,
                    ["subsumes"] = 3,
                    ["true"] = 447,
                    ["variant"] = 13,
                },
                outcomeKinds
            );

            var supportByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string path, string source) in sourceByPath)
            {
                if (LogtalkTestAdapter.TryReadSupportProgram(source, out string support))
                {
                    supportByPath.Add(path, support);
                }
            }

            HashSet<(string SourcePath, string Id)> conditionalAlternates =
            [
                .. declarations
                    .Where(test => !test.Disabled)
                    .GroupBy(test => (test.SourcePath, test.Id))
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
            ];
            LogtalkTestDeclaration[] directCases =
            [
                .. declarations.Where(test =>
                    !test.Disabled
                    && test.OutcomeKind
                        is "true"
                            or "false"
                            or "fail"
                            or "error"
                            or "errors"
                            or "ball"
                            or "exists"
                            or "subsumes"
                            or "variant"
                            or "deterministic"
                    && supportByPath.ContainsKey(test.SourcePath)
                    && LogtalkTestAdapter.TryUnwrapBackendGoal(test, out _)
                    && (
                        !conditionalAlternates.Contains((test.SourcePath, test.Id))
                        || IsApplicableConditionalBranch(test)
                    )
                ),
            ];

            Assert.Equal(505, directCases.Length);
            Assert.Equal(303, directCases.Count(test => test.OutcomeKind == "true"));
            Assert.Equal(72, directCases.Count(test => test.OutcomeKind == "false"));
            Assert.Equal(3, directCases.Count(test => test.OutcomeKind == "fail"));
            Assert.Equal(71, directCases.Count(test => test.OutcomeKind == "error"));
            Assert.Equal(11, directCases.Count(test => test.OutcomeKind == "variant"));
            Assert.Equal(41, directCases.Count(test => test.OutcomeKind == "exists"));
            Assert.Equal(3, directCases.Count(test => test.OutcomeKind == "subsumes"));
            Assert.Single(directCases, test => test.OutcomeKind == "deterministic");

            string? selectedId = Environment.GetEnvironmentVariable(CaseVariable);
            LogtalkTestDeclaration[] casesToExecute = SelectCasesToExecute(directCases, selectedId);

            var failures = new List<string>();
            var executionResults = new Dictionary<LogtalkTestDeclaration, string>();
            var engines = new Dictionary<string, PrologEngine>(StringComparer.Ordinal);
            foreach (LogtalkTestDeclaration test in casesToExecute)
            {
                if (
                    !TryGetSourceEngine(test, supportByPath[test.SourcePath], engines, out PrologEngine engine, out string failure)
                    || !Execute(test, engine, out failure)
                )
                {
                    failures.Add($"{test.SourcePath} | {test.Id} | {failure}");
                    executionResults.Add(test, $"failed: {failure}");
                }
                else
                {
                    executionResults.Add(test, "passed");
                }
            }

            string? reportPath = Environment.GetEnvironmentVariable(ReportVariable);
            if (reportPath is not null)
            {
                Assert.Null(selectedId);
                await WriteReportAsync(reportPath, declarations, directCases, executionResults);
            }

            Assert.True(failures.Count == 0, $"Independent ISO cases failed:\n{string.Join('\n', failures)}");
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
        }
    }

    private static bool IsApplicableConditionalBranch(LogtalkTestDeclaration declaration)
    {
        if (declaration.ConditionalGoal is null)
        {
            throw new InvalidDataException(
                $"{declaration.SourcePath}: duplicate declaration {declaration.Id} is not conditional."
            );
        }

        var engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        string condition = LogtalkTestAdapter.TranslateConditionalGoal(declaration.ConditionalGoal);
        try
        {
            RunResult result = engine.RunGoal(condition, out IReadOnlyList<DotProlog.Syntax.Diagnostic> diagnostics);
            if (diagnostics.Count > 0)
            {
                return false;
            }

            return result == RunResult.Success;
        }
        catch (PrologException)
        {
            // A Logtalk flag or other wrapper-only condition cannot select a backend branch.
            return false;
        }
    }

    private static LogtalkTestDeclaration[] SelectCasesToExecute(
        LogtalkTestDeclaration[] directCases,
        string? selectedId
    )
    {
        if (selectedId is null)
        {
            return directCases;
        }

        LogtalkTestDeclaration[] selected = [.. directCases.Where(test => test.Id == selectedId)];
        Assert.Single(selected);

        LogtalkTestDeclaration target = selected[0];
        LogtalkTestDeclaration[] sourceCases = [.. directCases.Where(test => test.SourcePath == target.SourcePath)];
        int targetIndex = Array.IndexOf(sourceCases, target);
        return sourceCases[..(targetIndex + 1)];
    }

    private static bool TryGetSourceEngine(
        LogtalkTestDeclaration test,
        string supportProgram,
        Dictionary<string, PrologEngine> engines,
        out PrologEngine engine,
        out string failure
    )
    {
        if (engines.TryGetValue(test.SourcePath, out engine!))
        {
            failure = string.Empty;
            return true;
        }

        engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        if (supportProgram.Length > 0)
        {
            LoadResult loaded = engine.ConsultText(supportProgram, test.SourcePath);
            if (!loaded.Success)
            {
                failure = $"support program did not compile: {string.Join("; ", loaded.Diagnostics)}";
                return false;
            }

            engine.RunPendingGoals();
        }

        engines.Add(test.SourcePath, engine);
        failure = string.Empty;
        return true;
    }

    private static async Task WriteReportAsync(
        string path,
        LogtalkTestDeclaration[] declarations,
        LogtalkTestDeclaration[] directCases,
        Dictionary<LogtalkTestDeclaration, string> executionResults
    )
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new
        {
            repository = Repository,
            tag = Tag,
            commit = Commit,
            summary = new
            {
                total = declarations.Length,
                enabled = declarations.Count(test => !test.Disabled),
                disabled = declarations.Count(test => test.Disabled),
                passed = executionResults.Count(result => result.Value == "passed"),
                failed = executionResults.Count(result => result.Value.StartsWith("failed:", StringComparison.Ordinal)),
                unsupported = declarations.Count(test => !test.Disabled && !directCases.Contains(test)),
            },
            cases = declarations.Select(test => new
            {
                source = test.SourcePath,
                id = test.Id,
                expectation = test.Outcome,
                status = test.Disabled ? "upstream-disabled"
                : executionResults.TryGetValue(test, out string? result) ? result
                : "unsupported",
            }),
        };

        string json = JsonSerializer.Serialize(report, ReportJsonOptions);
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
    }

    private static bool Execute(LogtalkTestDeclaration test, PrologEngine engine, out string failure)
    {
        if (!LogtalkTestAdapter.TryUnwrapBackendGoal(test, out string goal))
        {
            failure = "adapter did not find one backend goal";
            return false;
        }

        string assertion = test.OutcomeKind switch
        {
            "true" when test.Outcome == "true" => $"({goal})",
            "true" => $"(({goal}), ({LogtalkTestAdapter.TranslateAssertion(ArgumentOf(test.Outcome, "true"))}))",
            "exists" => $"(({goal}), ({LogtalkTestAdapter.TranslateAssertion(ArgumentOf(test.Outcome, "exists"))}))",
            "false" or "fail" => $"\\+ ({goal})",
            "error" => $"catch((({goal}), fail), error(ExternalError, _), ExternalError = ({ArgumentOf(test.Outcome, "error")}))",
            "errors" =>
                $"catch((({goal}), fail), ExternalBall, "
                    + $"member(ExternalBall, {LogtalkTestAdapter.TranslateErrorAlternatives(ArgumentOf(test.Outcome, "errors"))}))",
            "ball" => $"catch((({goal}), fail), ExternalBall, ExternalBall = ({ArgumentOf(test.Outcome, "ball")}))",
            "subsumes" => SubsumesAssertion(goal, test.Outcome),
            "variant" => VariantAssertion(goal, test.Outcome),
            "deterministic" when test.Outcome == "deterministic" => $"({goal})",
            _ => throw new InvalidOperationException($"Unsupported direct expectation: {test.Outcome}"),
        };

        try
        {
            RunResult result = engine.RunGoal(assertion, out IReadOnlyList<DotProlog.Syntax.Diagnostic> diagnostics);
            if (diagnostics.Count > 0)
            {
                failure = $"adapter goal did not compile: {string.Join("; ", diagnostics)} | {assertion}";
                return false;
            }

            if (result != RunResult.Success)
            {
                failure = $"expected {test.Outcome}, got {result} | {assertion}";
                return false;
            }

            if (test.OutcomeKind == "deterministic" && engine.Machine.HasAlternatives)
            {
                failure = $"expected deterministic success, but the goal left a choice point | {assertion}";
                return false;
            }
        }
        catch (PrologException exception)
        {
            failure = $"uncaught {exception.Message} | {assertion}";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static string VariantAssertion(string goal, string outcome)
    {
        string arguments = ArgumentOf(outcome, "variant");
        int separator = LogtalkTestAdapter.FindArgumentSeparator(arguments);
        if (separator < 0)
        {
            throw new InvalidDataException($"Malformed variant expectation: {outcome}");
        }

        string left = arguments[..separator].Trim();
        string right = arguments[(separator + 1)..].Trim();
        return $"(({goal}), subsumes_term(({left}), ({right})), subsumes_term(({right}), ({left})))";
    }

    private static string SubsumesAssertion(string goal, string outcome)
    {
        string arguments = ArgumentOf(outcome, "subsumes");
        int separator = LogtalkTestAdapter.FindArgumentSeparator(arguments);
        if (separator < 0)
        {
            throw new InvalidDataException($"Malformed subsumes expectation: {outcome}");
        }

        string expected = arguments[..separator].Trim();
        string actual = arguments[(separator + 1)..].Trim();
        return $"(({goal}), subsumes_term(({expected}), ({actual})))";
    }

    private static string ArgumentOf(string outcome, string functor)
    {
        string prefix = $"{functor}(";
        if (!outcome.StartsWith(prefix, StringComparison.Ordinal) || !outcome.EndsWith(')'))
        {
            throw new InvalidDataException($"Malformed {functor} expectation: {outcome}");
        }

        return outcome[prefix.Length..^1];
    }
}
