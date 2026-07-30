namespace Integration.Tests;

/// <summary>
/// Pins and inventories the independently maintained Logtalk ISO Prolog tests before their wrapper
/// is adapted to DotProlog execution.
/// </summary>
public sealed class LogtalkConformanceTests
{
    private const string OptInVariable = "DOTPROLOG_RUN_EXTERNAL_CONFORMANCE_TESTS";
    private const string Repository = "https://github.com/LogtalkDotOrg/logtalk3.git";
    private const string Tag = "lgt31010stable";
    private const string Commit = "11dfd24eb6673250be996012489e65c0f9370a7c";

    [Fact]
    public void AdapterPreservesDeclarationsExpectationsAndCharacterCodeSyntax()
    {
        const string source = """
            % A dot and comma inside terms are not declaration boundaries.
            test(iso_fixture_01, true(X == 1.0)) :-
                {X = 1.0}.

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
                Assert.Equal("{X = 1.0}", first.Body);
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
    }

    [Fact]
    public async Task PinnedIsoCorpusIsCompleteAndMechanicallyReadable()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to inventory the pinned independent conformance suite."
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

            LogtalkTestDeclaration[] declarations =
            [
                .. files.SelectMany(file =>
                    LogtalkTestAdapter.ReadDeclarations(
                        File.ReadAllText(file),
                        Path.GetRelativePath(testsRoot, file).Replace('\\', '/')
                    )
                ),
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
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
        }
    }
}
