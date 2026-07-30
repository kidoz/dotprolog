using System.Globalization;
using System.Text;
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

        const string operatorSupportSource = """
            :- op(100, fx, fx).
            :- object(tests).
            test(iso_operator, true) :-
                {true}.
            :- end_object.
            """;
        Assert.True(LogtalkTestAdapter.TryReadSupportProgram(operatorSupportSource, out string operatorSupport));
        Assert.Equal(":- op(100, fx, fx).", operatorSupport);

        const string osHelperSource = """
            portability_helper :-
                os::operating_system_type(unix).
            test(iso_without_portability_helper, true) :-
                {true}.
            """;
        Assert.True(LogtalkTestAdapter.TryReadSupportProgram(osHelperSource, out string osHelperSupport));
        Assert.Empty(osHelperSupport);

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
        Assert.Equal("catch((1^true), _, fail)", LogtalkTestAdapter.TranslateConditionalGoal(conditional[0].ConditionalGoal!));
        Assert.Equal(
            "(current_logtalk_flag(prolog_dialect, Dialect), '$logtalk_is_windows')",
            LogtalkTestAdapter.TranslateConditionalGoal(
                "(current_logtalk_flag(prolog_dialect, Dialect), os::operating_system_type(windows))"
            )
        );
        Assert.Equal(
            "[error((type_error(callable, 1)), _), error((type_error(callable, ':'(user, 1))), _)]",
            LogtalkTestAdapter.TranslateErrorAlternatives("[type_error(callable, 1), type_error(callable, ':'(user, 1))]")
        );

        const string findallSource = """
            test(iso_findall_escape, variant(L, [1, 2])) :-
                findall(X, {between(1, 2, X)}, L).
            """;
        LogtalkTestDeclaration findall = Assert.Single(LogtalkTestAdapter.ReadDeclarations(findallSource, "findall.lgt"));
        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(findall, out string findallGoal));
        Assert.Equal("findall(X, ((between(1, 2, X))), L)", findallGoal);

        const string mixedSource = """
            helper(a).
            test(iso_mixed_escape, true) :-
                helper(X),
                {atom(X)}.
            """;
        LogtalkTestDeclaration mixed = Assert.Single(LogtalkTestAdapter.ReadDeclarations(mixedSource, "mixed.lgt"));
        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(mixed, out string mixedGoal));
        Assert.Equal(
            """
            helper(X),
                (atom(X))
            """,
            mixedGoal
        );

        const string helperSource = """
            test(iso_helpers, true) :-
                {term_variables(A+B+B, [B|Vars])},
                ^^assertion(A == B),
                assertion(Vars == [B]),
                (variant(Pair, pair(_, _)) -> true; fail).
            """;
        LogtalkTestDeclaration helper = Assert.Single(LogtalkTestAdapter.ReadDeclarations(helperSource, "helpers.lgt"));
        Assert.True(LogtalkTestAdapter.TryUnwrapBackendGoal(helper, out string helperGoal));
        Assert.Equal(
            """
            (term_variables(A+B+B, [B|Vars])),
                (A == B),
                (Vars == [B]),
                ((subsumes_term((Pair), (pair(_, _))), subsumes_term((pair(_, _)), (Pair))) -> true; fail)
            """,
            helperGoal
        );

        const string unrelatedDispatchSource = """
            test(iso_dispatch, true) :-
                ^^unsupported_helper(output),
                {write(a)}.
            """;
        LogtalkTestDeclaration unrelatedDispatch = Assert.Single(
            LogtalkTestAdapter.ReadDeclarations(unrelatedDispatchSource, "dispatch.lgt")
        );
        Assert.False(LogtalkTestAdapter.TryUnwrapBackendGoal(unrelatedDispatch, out _));

        var textInputEngine = CreateAdapterEngine(Path.GetTempPath());
        Assert.Equal(
            RunResult.Success,
            textInputEngine.RunGoal(
                "'$logtalk_set_text_input'(['a. ', 'b.']), read(A), read(B), A == a, B == b",
                out IReadOnlyList<DotProlog.Syntax.Diagnostic> textInputDiagnostics
            )
        );
        Assert.Empty(textInputDiagnostics);

        Assert.Equal(
            RunResult.Success,
            textInputEngine.RunGoal(
                "'$logtalk_set_text_input'(input_alias, 'a.'), read(input_alias, a), "
                    + "close(input_alias), '$logtalk_delete_text_input'(input_alias)",
                out IReadOnlyList<DotProlog.Syntax.Diagnostic> namedTextInputDiagnostics
            )
        );
        Assert.Empty(namedTextInputDiagnostics);

        Assert.Equal(
            RunResult.Success,
            textInputEngine.RunGoal(
                "'$logtalk_set_text_output'(q), write(w), " + "'$logtalk_text_output_assertion'(qw, Assertion), call(Assertion)",
                out IReadOnlyList<DotProlog.Syntax.Diagnostic> textOutputDiagnostics
            )
        );
        Assert.Empty(textOutputDiagnostics);

        Assert.Equal(
            RunResult.Success,
            textInputEngine.RunGoal(
                "'$logtalk_set_text_output'(out, q), write(out, w), "
                    + "'$logtalk_text_output_assertion'(out, qw, Assertion), call(Assertion)",
                out IReadOnlyList<DotProlog.Syntax.Diagnostic> namedTextOutputDiagnostics
            )
        );
        Assert.Empty(namedTextOutputDiagnostics);

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

        var ball = new LogtalkTestDeclaration("fixture.lgt", "iso_ball", "ball(bla)", null, "{throw(bla)}", false, null);
        Assert.True(Execute(ball, adapterEngine, out string ballFailure), ballFailure);

        const string quickCheckSource = """
            twice(T) :-
                {term_variables(T, Vs1), term_variables(T, Vs2), Vs1 == Vs2}.
            quick_check(iso_quick_fixture, twice(+term)).
            """;
        Assert.True(LogtalkTestAdapter.TryReadSupportProgram(quickCheckSource, out string quickCheckSupport));
        var quickCheckEngine = CreateAdapterEngine(Path.GetTempPath());
        quickCheckEngine.ConsultOrThrow(quickCheckSupport, "quick-check-fixture.lgt");
        LogtalkTestDeclaration quickCheck = Assert.Single(
            LogtalkTestAdapter.ReadDeclarations(quickCheckSource, "quick-check-fixture.lgt")
        );
        Assert.True(ExecuteQuickCheck(quickCheck, quickCheckEngine, out string quickCheckFailure), quickCheckFailure);
    }

    [Fact]
    public async Task PinnedIsoCorpusIsCompleteAndDirectCasesExecute()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the pinned independent conformance suite."
        );

        string checkout = Path.Combine(Path.GetTempPath(), $"dotprolog-logtalk-{Environment.ProcessId}");
        string adapterFilesRoot = Path.Combine(checkout, ".dotprolog-adapter");
        var engines = new Dictionary<string, PrologEngine>(StringComparer.Ordinal);
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
            HashSet<LogtalkTestDeclaration> applicableConditionalCases =
            [
                .. declarations.Where(test =>
                    !test.Disabled
                    && conditionalAlternates.Contains((test.SourcePath, test.Id))
                    && IsApplicableConditionalBranch(test)
                ),
            ];
            HashSet<LogtalkTestDeclaration> nonApplicableCases =
            [
                .. declarations.Where(test =>
                    !test.Disabled
                    && conditionalAlternates.Contains((test.SourcePath, test.Id))
                    && !applicableConditionalCases.Contains(test)
                ),
            ];
            Assert.Equal(5, nonApplicableCases.Count);

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
                            or "quick_check"
                    && supportByPath.ContainsKey(test.SourcePath)
                    && (test.OutcomeKind == "quick_check" || LogtalkTestAdapter.TryUnwrapBackendGoal(test, out _))
                    && (!conditionalAlternates.Contains((test.SourcePath, test.Id)) || applicableConditionalCases.Contains(test))
                ),
            ];

            Assert.Equal(754, directCases.Length);
            Assert.Equal(442, directCases.Count(test => test.OutcomeKind == "true"));
            Assert.Equal(73, directCases.Count(test => test.OutcomeKind == "false"));
            Assert.Equal(3, directCases.Count(test => test.OutcomeKind == "fail"));
            Assert.Equal(154, directCases.Count(test => test.OutcomeKind == "error"));
            Assert.Equal(13, directCases.Count(test => test.OutcomeKind == "variant"));
            Assert.Equal(41, directCases.Count(test => test.OutcomeKind == "exists"));
            Assert.Equal(3, directCases.Count(test => test.OutcomeKind == "subsumes"));
            Assert.Single(directCases, test => test.OutcomeKind == "deterministic");
            Assert.Equal(17, directCases.Count(test => test.OutcomeKind == "errors"));
            Assert.Single(directCases, test => test.OutcomeKind == "ball");
            Assert.Equal(6, directCases.Count(test => test.OutcomeKind == "quick_check"));

            string? selectedId = Environment.GetEnvironmentVariable(CaseVariable);
            LogtalkTestDeclaration[] casesToExecute = SelectCasesToExecute(directCases, selectedId);

            var failures = new List<string>();
            var executionResults = new Dictionary<LogtalkTestDeclaration, string>();
            foreach (LogtalkTestDeclaration test in casesToExecute)
            {
                if (
                    !TryGetSourceEngine(
                        test,
                        supportByPath[test.SourcePath],
                        adapterFilesRoot,
                        engines,
                        out PrologEngine engine,
                        out string failure
                    ) || !Execute(test, engine, out failure)
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
                await WriteReportAsync(reportPath, declarations, directCases, nonApplicableCases, executionResults);
            }

            Assert.True(failures.Count == 0, $"Independent ISO cases failed:\n{string.Join('\n', failures)}");
        }
        finally
        {
            foreach (PrologEngine engine in engines.Values)
            {
                engine.Machine.Streams.CloseAll();
            }

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
        engine.Program.Builtins.Register("current_logtalk_flag", 2, CurrentLogtalkFlag);
        engine.Program.Builtins.Register("$logtalk_is_windows", 0, static _ => OperatingSystem.IsWindows());
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

    private static bool CurrentLogtalkFlag(Machine machine)
    {
        Cell flag = machine.Argument(0);
        if (flag.Tag != CellTag.Atom || machine.Symbols.AtomName(flag.Index) != "prolog_dialect")
        {
            return false;
        }

        return machine.Unify(machine.Argument(1), Cell.Atom(machine.Symbols.InternAtom("dotprolog")));
    }

    private static LogtalkTestDeclaration[] SelectCasesToExecute(LogtalkTestDeclaration[] directCases, string? selectedId)
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
        string adapterFilesRoot,
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

        engine = CreateAdapterEngine(adapterFilesRoot);
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

    private static PrologEngine CreateAdapterEngine(string filesRoot)
    {
        var engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        var namedInputPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var fixturePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        engine.Program.Builtins.Register("$logtalk_set_text_input", 1, SetTextInput);
        engine.Program.Builtins.Register("$logtalk_set_text_input", 2, machine => SetNamedTextInput(machine, namedInputPaths));
        engine.Program.Builtins.Register(
            "$logtalk_delete_text_input",
            1,
            machine => DeleteNamedTextInput(machine, namedInputPaths)
        );
        engine.Program.Builtins.Register("$logtalk_set_text_output", 1, SetTextOutput);
        engine.Program.Builtins.Register("$logtalk_set_text_output", 2, SetNamedTextOutput);
        engine.Program.Builtins.Register("$logtalk_text_output_assertion", 2, TextOutputAssertion);
        engine.Program.Builtins.Register("$logtalk_text_output_assertion", 3, NamedTextOutputAssertion);
        engine.Program.Builtins.Register("$logtalk_text_output_contents", 1, TextOutputContents);
        engine.Program.Builtins.Register("$logtalk_text_output_contents", 2, NamedTextOutputContents);
        engine.Program.Builtins.Register("$logtalk_check_text_output", 2, CheckNamedTextOutput);
        engine.Program.Builtins.Register("$logtalk_file_path", 2, machine => FilePath(machine, filesRoot, fixturePaths));
        engine.Program.Builtins.Register("$logtalk_create_text_file", 2, CreateTextFile);
        engine.Program.Builtins.Register("$logtalk_create_binary_file", 2, CreateBinaryFile);
        engine.Program.Builtins.Register("$logtalk_closed_input_stream", 2, static machine => ClosedStream(machine, input: true));
        engine.Program.Builtins.Register(
            "$logtalk_closed_output_stream",
            2,
            static machine => ClosedStream(machine, input: false)
        );
        engine.Program.Builtins.Register("$logtalk_create_binary_output", 2, machine => CreateBinaryOutput(machine, filesRoot));
        engine.Program.Builtins.Register(
            "$logtalk_set_named_binary_output",
            2,
            machine => SetNamedBinaryOutput(machine, filesRoot)
        );
        engine.Program.Builtins.Register("$logtalk_binary_output_assertion", 2, BinaryOutputAssertion);
        engine.Program.Builtins.Register("$logtalk_binary_output_assertion", 3, NamedBinaryOutputAssertion);
        engine.Program.Builtins.Register(
            "$logtalk_suppress_text_output",
            0,
            static machine =>
            {
                machine.Output = TextWriter.Null;
                return true;
            }
        );
        return engine;
    }

    private static bool SetTextInput(Machine machine)
    {
        if (!TryReadTextContents(machine, machine.Argument(0), out string input))
        {
            return false;
        }

        machine.Input = new StringReader(input);
        return true;
    }

    private static bool SetNamedTextInput(Machine machine, Dictionary<string, string> paths)
    {
        Cell alias = machine.Argument(0);
        if (alias.Tag != CellTag.Atom || !TryReadTextContents(machine, machine.Argument(1), out string input))
        {
            return false;
        }

        string aliasName = machine.Symbols.AtomName(alias.Index);
        string path = Path.GetTempFileName();
        File.WriteAllText(path, input);
        _ = machine.Streams.Open(path, "read", aliasName, "text", reposition: false);
        paths.Add(aliasName, path);
        return true;
    }

    private static bool DeleteNamedTextInput(Machine machine, Dictionary<string, string> paths)
    {
        Cell alias = machine.Argument(0);
        if (alias.Tag != CellTag.Atom || !paths.Remove(machine.Symbols.AtomName(alias.Index), out string? path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static bool SetTextOutput(Machine machine)
    {
        if (!TryReadTextContents(machine, machine.Argument(0), out string initial))
        {
            return false;
        }

        machine.Output = new StringWriter(new StringBuilder(initial));
        return true;
    }

    private static bool SetNamedTextOutput(Machine machine)
    {
        Cell alias = machine.Argument(0);
        if (alias.Tag != CellTag.Atom || !TryReadTextContents(machine, machine.Argument(1), out string initial))
        {
            return false;
        }

        string path = Path.GetTempFileName();
        PrologStream stream = machine.Streams.Open(
            path,
            "write",
            machine.Symbols.AtomName(alias.Index),
            "text",
            reposition: false
        );
        stream.Writer!.Write(initial);
        return true;
    }

    private static bool TextOutputAssertion(Machine machine)
    {
        if (machine.Output is not StringWriter output)
        {
            return false;
        }

        Cell assertion = EqualityAssertion(machine, machine.Argument(0), output.ToString());
        machine.Output = TextWriter.Null;
        return machine.Unify(machine.Argument(1), assertion);
    }

    private static bool NamedTextOutputAssertion(Machine machine)
    {
        if (!TryTakeNamedTextOutput(machine, machine.Argument(0), out string output))
        {
            return false;
        }

        Cell assertion = EqualityAssertion(machine, machine.Argument(1), output);
        return machine.Unify(machine.Argument(2), assertion);
    }

    private static bool TextOutputContents(Machine machine)
    {
        if (machine.Output is not StringWriter output)
        {
            return false;
        }

        Cell contents = CharacterList(machine, output.ToString());
        machine.Output = TextWriter.Null;
        return machine.Unify(machine.Argument(0), contents);
    }

    private static bool NamedTextOutputContents(Machine machine)
    {
        if (!TryTakeNamedTextOutput(machine, machine.Argument(0), out string output))
        {
            return false;
        }

        return machine.Unify(machine.Argument(1), CharacterList(machine, output));
    }

    private static bool CheckNamedTextOutput(Machine machine)
    {
        if (
            !TryTakeNamedTextOutput(machine, machine.Argument(0), out string output)
            || !TryReadTextContents(machine, machine.Argument(1), out string expected)
        )
        {
            return false;
        }

        return output == expected;
    }

    private static bool FilePath(Machine machine, string filesRoot, Dictionary<string, string> fixturePaths)
    {
        Cell name = machine.Argument(0);
        if (name.Tag != CellTag.Atom)
        {
            return false;
        }

        string fixture = machine.Symbols.AtomName(name.Index);
        if (!fixturePaths.TryGetValue(fixture, out string? path))
        {
            Directory.CreateDirectory(filesRoot);
            path = Path.Combine(filesRoot, $"{Guid.NewGuid():N}.tmp");
            fixturePaths.Add(fixture, path);
        }

        return machine.Unify(machine.Argument(1), Cell.Atom(machine.Symbols.InternAtom(path.Replace('\\', '/'))));
    }

    private static bool CreateTextFile(Machine machine)
    {
        Cell path = machine.Argument(0);
        if (path.Tag != CellTag.Atom || !TryReadTextContents(machine, machine.Argument(1), out string contents))
        {
            return false;
        }

        File.WriteAllText(machine.Symbols.AtomName(path.Index), contents);
        return true;
    }

    private static bool CreateBinaryFile(Machine machine)
    {
        Cell path = machine.Argument(0);
        if (path.Tag != CellTag.Atom || !TryReadBytes(machine, machine.Argument(1), out byte[] contents))
        {
            return false;
        }

        File.WriteAllBytes(machine.Symbols.AtomName(path.Index), contents);
        return true;
    }

    private static bool ClosedStream(Machine machine, bool input)
    {
        Cell options = machine.Argument(1);
        if (options.Tag != CellTag.Atom || options.Index != machine.Symbols.EmptyList)
        {
            return false;
        }

        string path = Path.GetTempFileName();
        PrologStream stream = machine.Streams.Open(path, input ? "read" : "write", alias: null, "text", reposition: false);
        Cell handle = machine.CreateStructure(machine.Symbols.InternFunctor("$stream", 1), [Cell.Integer60(stream.Id)]);
        machine.Streams.Close(stream);
        File.Delete(path);
        return machine.Unify(machine.Argument(0), handle);
    }

    private static bool CreateBinaryOutput(Machine machine, string filesRoot)
    {
        if (
            !TryReadBytes(machine, machine.Argument(0), out byte[] initial)
            || !TryOpenBinaryOutput(machine, filesRoot, alias: null, initial, out PrologStream stream)
        )
        {
            return false;
        }

        Cell handle = machine.CreateStructure(machine.Symbols.InternFunctor("$stream", 1), [Cell.Integer60(stream.Id)]);
        return machine.Unify(machine.Argument(1), handle);
    }

    private static bool SetNamedBinaryOutput(Machine machine, string filesRoot)
    {
        Cell alias = machine.Argument(0);
        return alias.Tag == CellTag.Atom
            && TryReadBytes(machine, machine.Argument(1), out byte[] initial)
            && TryOpenBinaryOutput(machine, filesRoot, machine.Symbols.AtomName(alias.Index), initial, out _);
    }

    private static bool TryOpenBinaryOutput(
        Machine machine,
        string filesRoot,
        string? alias,
        byte[] initial,
        out PrologStream stream
    )
    {
        Directory.CreateDirectory(filesRoot);
        string path = Path.Combine(filesRoot, $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, initial);
        stream = machine.Streams.Open(path, "append", alias, "binary", reposition: false);
        return true;
    }

    private static bool BinaryOutputAssertion(Machine machine)
    {
        if (!TryTakeBinaryOutput(machine, machine.Streams.CurrentOutput, out byte[] output))
        {
            return false;
        }

        return machine.Unify(machine.Argument(1), EqualityAssertion(machine, machine.Argument(0), ByteList(machine, output)));
    }

    private static bool NamedBinaryOutputAssertion(Machine machine)
    {
        Cell alias = machine.Argument(0);
        if (
            alias.Tag != CellTag.Atom
            || !TryTakeBinaryOutput(machine, machine.Streams.ByAlias(machine.Symbols.AtomName(alias.Index)), out byte[] output)
        )
        {
            return false;
        }

        return machine.Unify(machine.Argument(2), EqualityAssertion(machine, machine.Argument(1), ByteList(machine, output)));
    }

    private static bool TryTakeBinaryOutput(Machine machine, PrologStream? stream, out byte[] output)
    {
        if (stream is null || stream.IsPermanent)
        {
            output = [];
            return false;
        }

        string path = stream.Name;
        machine.Streams.Close(stream);
        output = File.ReadAllBytes(path);
        File.Delete(path);
        return true;
    }

    private static Cell ByteList(Machine machine, byte[] bytes)
    {
        Cell[] items = [.. bytes.Select(value => Cell.Integer60(value))];
        return machine.CreateList(items, Cell.Atom(machine.Symbols.EmptyList));
    }

    private static Cell EqualityAssertion(Machine machine, Cell expected, Cell actual) =>
        machine.CreateStructure(machine.Symbols.InternFunctor("==", 2), [expected, actual]);

    private static bool TryReadBytes(Machine machine, Cell item, out byte[] contents)
    {
        var bytes = new List<byte>();

        while (item.Tag == CellTag.Structure && machine.HeapAt(item.Index).Index == machine.Symbols.ListFunctor)
        {
            Cell head = machine.Dereference(machine.HeapAt(item.Index + 1));
            if (head.Tag != CellTag.Integer || head.Integer is < byte.MinValue or > byte.MaxValue)
            {
                contents = [];
                return false;
            }

            bytes.Add((byte)head.Integer);
            item = machine.Dereference(machine.HeapAt(item.Index + 2));
        }

        if (item.Tag != CellTag.Atom || item.Index != machine.Symbols.EmptyList)
        {
            contents = [];
            return false;
        }

        contents = [.. bytes];
        return true;
    }

    private static bool TryTakeNamedTextOutput(Machine machine, Cell alias, out string output)
    {
        if (alias.Tag != CellTag.Atom)
        {
            output = string.Empty;
            return false;
        }

        PrologStream? stream = machine.Streams.ByAlias(machine.Symbols.AtomName(alias.Index));
        if (stream?.Writer is null)
        {
            output = string.Empty;
            return false;
        }

        string path = stream.Name;
        machine.Streams.Close(stream);
        output = File.ReadAllText(path);
        File.Delete(path);
        return true;
    }

    private static Cell EqualityAssertion(Machine machine, Cell expected, string actual) =>
        machine.CreateStructure(
            machine.Symbols.InternFunctor("==", 2),
            [expected, Cell.Atom(machine.Symbols.InternAtom(actual))]
        );

    private static Cell CharacterList(Machine machine, string text)
    {
        Cell[] characters = [.. text.Select(character => Cell.Atom(machine.Symbols.InternAtom(character.ToString())))];
        return machine.CreateList(characters, Cell.Atom(machine.Symbols.EmptyList));
    }

    private static bool TryReadTextContents(Machine machine, Cell item, out string contents)
    {
        var text = new StringBuilder();

        if (item.Tag == CellTag.Atom)
        {
            text.Append(machine.Symbols.AtomName(item.Index));
        }
        else
        {
            while (item.Tag == CellTag.Structure && machine.HeapAt(item.Index).Index == machine.Symbols.ListFunctor)
            {
                Cell head = machine.Dereference(machine.HeapAt(item.Index + 1));
                if (head.Tag != CellTag.Atom)
                {
                    contents = string.Empty;
                    return false;
                }

                text.Append(machine.Symbols.AtomName(head.Index));
                item = machine.Dereference(machine.HeapAt(item.Index + 2));
            }

            if (item.Tag != CellTag.Atom || item.Index != machine.Symbols.EmptyList)
            {
                contents = string.Empty;
                return false;
            }
        }

        contents = text.ToString();
        return true;
    }

    private static async Task WriteReportAsync(
        string path,
        LogtalkTestDeclaration[] declarations,
        LogtalkTestDeclaration[] directCases,
        HashSet<LogtalkTestDeclaration> nonApplicableCases,
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
                applicable = declarations.Count(test => !test.Disabled && !nonApplicableCases.Contains(test)),
                disabled = declarations.Count(test => test.Disabled),
                not_applicable = nonApplicableCases.Count,
                passed = executionResults.Count(result => result.Value == "passed"),
                failed = executionResults.Count(result => result.Value.StartsWith("failed:", StringComparison.Ordinal)),
                unsupported = declarations.Count(test =>
                    !test.Disabled && !nonApplicableCases.Contains(test) && !directCases.Contains(test)
                ),
            },
            cases = declarations.Select(test => new
            {
                source = test.SourcePath,
                id = test.Id,
                expectation = test.Outcome,
                status = test.Disabled ? "upstream-disabled"
                : nonApplicableCases.Contains(test) ? "not-applicable"
                : executionResults.TryGetValue(test, out string? result) ? result
                : "unsupported",
            }),
        };

        string json = JsonSerializer.Serialize(report, ReportJsonOptions);
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
    }

    private static bool Execute(LogtalkTestDeclaration test, PrologEngine engine, out string failure)
    {
        if (test.OutcomeKind == "quick_check")
        {
            return ExecuteQuickCheck(test, engine, out failure);
        }

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
            "errors" => $"catch((({goal}), fail), ExternalBall, "
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

    private static bool ExecuteQuickCheck(LogtalkTestDeclaration test, PrologEngine engine, out string failure)
    {
        (string property, IReadOnlyList<string> inputs) = test.Id switch
        {
            "iso_term_variables_2_01" or "iso_quick_fixture" => ("twice", GenerateTermInputs(seed: 13211)),
            "iso_term_variables_2_02" => ("chain", GenerateTermInputs(seed: 13212)),
            "iso_number_chars_2_14" => ("round_trip", GenerateIntegerInputs(seed: 16714)),
            "iso_number_chars_2_15" => ("round_trip", GenerateFloatInputs(seed: 16715)),
            "iso_number_codes_2_12" => ("round_trip", GenerateIntegerInputs(seed: 16812)),
            "iso_number_codes_2_13" => ("round_trip", GenerateFloatInputs(seed: 16813)),
            _ => throw new InvalidOperationException($"Unsupported QuickCheck declaration: {test.Id}"),
        };

        for (int trial = 0; trial < inputs.Count; trial++)
        {
            string goal = $"{property}({inputs[trial]})";
            try
            {
                RunResult result = engine.RunGoal(goal, out IReadOnlyList<DotProlog.Syntax.Diagnostic> diagnostics);
                if (diagnostics.Count > 0)
                {
                    failure = $"QuickCheck trial {trial + 1} did not compile: " + $"{string.Join("; ", diagnostics)} | {goal}";
                    return false;
                }

                if (result != RunResult.Success)
                {
                    failure = $"QuickCheck trial {trial + 1} failed | {goal}";
                    return false;
                }
            }
            catch (PrologException exception)
            {
                failure = $"QuickCheck trial {trial + 1} raised {exception.Message} | {goal}";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> GenerateIntegerInputs(int seed)
    {
        string[] edgeCases =
        [
            "0",
            "1",
            "-1",
            Cell.MinInteger.ToString(CultureInfo.InvariantCulture),
            Cell.MaxInteger.ToString(CultureInfo.InvariantCulture),
        ];
        var random = new DeterministicRandom(seed);
        return
        [
            .. edgeCases,
            .. Enumerable
                .Range(0, 100 - edgeCases.Length)
                .Select(_ => random.Next(-1000, 1001).ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private static IReadOnlyList<string> GenerateFloatInputs(int seed)
    {
        string[] edgeCases = ["0.0", "1.0", "-1.0", "0.000000000001"];
        var random = new DeterministicRandom(seed);
        return
        [
            .. edgeCases,
            .. Enumerable
                .Range(0, 100 - edgeCases.Length)
                .Select(_ => FloatLiteral(random.Next(-1000, 1001) * random.NextDouble())),
        ];
    }

    private static IReadOnlyList<string> GenerateTermInputs(int seed)
    {
        var random = new DeterministicRandom(seed);
        return [.. Enumerable.Range(0, 100).Select(_ => GenerateTerm(random, depth: 3))];
    }

    private static string GenerateTerm(DeterministicRandom random, int depth)
    {
        string[] variables = ["A", "B", "C", "_"];
        string[] atoms = ["a", "z", "[]", "{}", "'a b'", "'\\\\'"];

        int shape = depth == 0 ? random.Next(4) : random.Next(9);
        return shape switch
        {
            0 => variables[random.Next(variables.Length)],
            1 => atoms[random.Next(atoms.Length)],
            2 => random.Next(-1000, 1001).ToString(CultureInfo.InvariantCulture),
            3 => FloatLiteral(random.Next(-1000, 1001) * random.NextDouble()),
            4 => $"f({GenerateTerm(random, depth - 1)})",
            5 => $"pair({GenerateTerm(random, depth - 1)},{GenerateTerm(random, depth - 1)})",
            6 => $"[{GenerateTerm(random, depth - 1)},{GenerateTerm(random, depth - 1)}]",
            7 => $"[{GenerateTerm(random, depth - 1)}|{GenerateTerm(random, depth - 1)}]",
            _ => $"node({GenerateTerm(random, depth - 1)},"
                + $"{GenerateTerm(random, depth - 1)},"
                + $"{GenerateTerm(random, depth - 1)})",
        };
    }

    private static string FloatLiteral(double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            return text;
        }

        int exponent = text.IndexOf('E', StringComparison.Ordinal);
        if (exponent < 0)
        {
            exponent = text.IndexOf('e', StringComparison.Ordinal);
        }

        return exponent >= 0 ? text.Insert(exponent, ".0") : $"{text}.0";
    }

    private sealed class DeterministicRandom(int seed)
    {
        private uint state = unchecked((uint)seed);

        public int Next(int maximum)
        {
            return Next(0, maximum);
        }

        public int Next(int minimum, int maximum)
        {
            uint range = checked((uint)(maximum - minimum));
            return minimum + (int)(NextUInt32() % range);
        }

        public double NextDouble()
        {
            return NextUInt32() / ((double)uint.MaxValue + 1.0);
        }

        private uint NextUInt32()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
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
