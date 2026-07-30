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
    private const string Repository = "https://github.com/LogtalkDotOrg/logtalk3.git";
    private const string Tag = "lgt31010stable";
    private const string Commit = "11dfd24eb6673250be996012489e65c0f9370a7c";

    [Fact]
    public void AdapterPreservesDeclarationsExpectationsAndCharacterCodeSyntax()
    {
        const string source = """
            % A dot and comma inside terms are not declaration boundaries.
            test(iso_fixture_01, true(X == 1.0)) :-
                {X = 1.0},
                {Y = pair(a, b)}.

            test(iso_fixture_02, error(type_error(character,0'.))) :-
                {char_code(0'., _)}.

            - test(iso_fixture_03, false, [note('upstream disabled')]) :-
                {fail}.
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
            }
        );

        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(declarations[0], out string firstGoal));
        Assert.Equal("(X = 1.0), (Y = pair(a, b))", firstGoal);
        Assert.Equal(
            "((abs((X) - (3.1415927)) < 0.0000000001) -> true ; (abs((X) - (3.1415927)) < (0.00001 * max(abs(X), abs(3.1415927)))))",
            LogtalkTestAdapter.TranslateAssertion("X =~= 3.1415927")
        );
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

            Assert.Equal(782, declarations.Length);
            Assert.Equal(753, declarations.Count(test => !test.Disabled));
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
                    ["subsumes"] = 3,
                    ["true"] = 447,
                    ["variant"] = 13,
                },
                outcomeKinds
            );

            LogtalkTestDeclaration[] directCases =
            [
                .. declarations.Where(test =>
                    !test.Disabled
                    && test.OutcomeKind is "true" or "false" or "error"
                    && LogtalkTestAdapter.CanExecuteWithoutSupportClauses(sourceByPath[test.SourcePath])
                    && LogtalkTestAdapter.TryUnwrapBackendGoal(test, out _)
                ),
            ];

            Assert.Equal(177, directCases.Length);
            Assert.Equal(100, directCases.Count(test => test.OutcomeKind == "true"));
            Assert.Equal(53, directCases.Count(test => test.OutcomeKind == "false"));
            Assert.Equal(24, directCases.Count(test => test.OutcomeKind == "error"));

            string? selectedId = Environment.GetEnvironmentVariable(CaseVariable);
            LogtalkTestDeclaration[] casesToExecute = selectedId is null
                ? directCases
                : directCases.Where(test => test.Id == selectedId).ToArray();
            Assert.True(
                selectedId is null || casesToExecute.Length > 0,
                $"{CaseVariable} did not select a directly executable case: {selectedId}"
            );

            var failures = new List<string>();
            foreach (LogtalkTestDeclaration test in casesToExecute)
            {
                if (!Execute(test, out string failure))
                {
                    failures.Add($"{test.SourcePath} | {test.Id} | {failure}");
                }
            }

            Assert.True(failures.Count == 0, $"Independent ISO cases failed:\n{string.Join('\n', failures)}");
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
        }
    }

    private static bool Execute(LogtalkTestDeclaration test, out string failure)
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
            "false" => $"\\+ ({goal})",
            "error" => $"catch((({goal}), fail), error(ExternalError, _), ExternalError = ({ArgumentOf(test.Outcome, "error")}))",
            _ => throw new InvalidOperationException($"Unsupported direct expectation: {test.Outcome}"),
        };

        var engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };

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
        }
        catch (PrologException exception)
        {
            failure = $"uncaught {exception.Message} | {assertion}";
            return false;
        }

        failure = string.Empty;
        return true;
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
