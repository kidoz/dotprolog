using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>The opt-in processor mode that rejects implementation-specific language features.</summary>
public sealed class StrictIsoTests
{
    [Fact]
    public void ExtendedModeRemainsTheDefault()
    {
        var engine = new PrologEngine();

        Assert.Equal(PrologLanguageMode.Extended, engine.Program.LanguageMode);
        Assert.True(engine.ConsultText("p :- member(a, [a]).").Success);
    }

    [Fact]
    public void StrictModeAcceptsStandardPredicatesAndControls()
    {
        var engine = StrictEngine();
        LoadResult loaded = engine.ConsultText(
            """
            values(L) :-
                findall(X, (X = 2 ; X = 1), Unsorted),
                once(sort(Unsorted, L)),
                atom_concat(dot, prolog, dotprolog).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal("values([1, 2])", out IReadOnlyList<DotProlog.Syntax.Diagnostic> diagnostics)
        );
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownLanguageModeIsRejectedBeforeProgramCreation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrologEngine((PrologLanguageMode)int.MaxValue));
    }

    [Fact]
    public void StrictModeRejectsAPredefinedLibraryExtension()
    {
        var engine = StrictEngine();

        LoadResult loaded = engine.ConsultText("p :- member(a, [a]).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("member/2", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("strict.pl", diagnostic.FileName);
    }

    [Fact]
    public void StrictModeAllowsAUserDefinitionWithAnExtensionLibraryName()
    {
        var engine = StrictEngine();
        LoadResult loaded = engine.ConsultText(
            """
            answer :- member(a, [a]).
            member(X, [X|_]).
            member(X, [_|Xs]) :- member(X, Xs).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("answer", out _));
    }

    [Fact]
    public void StrictModeRejectsSoftCutAsAPartOneControlConstruct()
    {
        var engine = StrictEngine();

        LoadResult loaded = engine.ConsultText("p :- (true *-> true ; fail).");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("*->/2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p :- a := b.")]
    [InlineData("p :- $ true.")]
    [InlineData("p(X) :- X = (a '.' b).")]
    public void StrictModeDoesNotPredefineExtendedOperators(string source)
    {
        var engine = StrictEngine();

        LoadResult loaded = engine.ConsultText(source, "strict.pl");

        Assert.False(loaded.Success);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.FileName == "strict.pl");
    }

    [Theory]
    [InlineData("p :- a := b.")]
    [InlineData("p :- $ true.")]
    [InlineData("p(X) :- X = (a '.' b).")]
    public void ExtendedModeKeepsItsPredefinedOperators(string source)
    {
        var engine = new PrologEngine();

        LoadResult loaded = engine.ConsultText(source, "extended.pl");

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
    }

    [Fact]
    public void StrictModeAcceptsAStandardMetaPredicateDeclaration()
    {
        var engine = StrictEngine();

        LoadResult loaded = engine.ConsultText(
            """
            :- module(meta_example, [answer/0]).
            :- meta_predicate twice(0).
            twice(Goal) :- call(Goal), call(Goal).
            answer :- twice(true).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("answer", out _));
    }

    [Fact]
    public void StrictModeAcceptsAStandardModuleImport()
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-strict-modules-").FullName;

        try
        {
            var library = Path.Combine(directory, "library.pl");
            var main = Path.Combine(directory, "main.pl");
            File.WriteAllText(library, ":- module(library, [answer/1]).\nanswer(42).\n");
            File.WriteAllText(
                main,
                ":- module(main, [result/1]).\n:- use_module(library, [answer/1]).\nresult(X) :- answer(X).\n"
            );

            var engine = StrictEngine();
            LoadResult loaded = engine.ConsultFile(main);

            Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
            Assert.Equal(RunResult.Success, engine.RunGoal("result(42)", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StrictModeStillAcceptsAStandardModuleText()
    {
        var engine = StrictEngine();

        LoadResult loaded = engine.ConsultText(
            """
            :- module(example, [answer/1]).
            answer(42).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("answer(42)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("example:answer(42)", out _));
    }

    [Fact]
    public void StrictModeRejectsAnExtensionInAHostTextualGoal()
    {
        var engine = StrictEngine();

        PrologException exception = Assert.Throws<PrologException>(() => engine.Query("member(a, [a])"));

        Assert.Contains(CompilerDiagnosticIds.StrictIsoViolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictModeRejectsARuntimeConstructedExtensionGoal()
    {
        var engine = StrictEngine();
        Assert.True(engine.ConsultText("invoke(Goal) :- call(Goal).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunGoal("invoke(member(a, [a]))", out _));

        Assert.Contains(
            "permission_error(access, implementation_specific_feature, member/2)",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void StrictModeRejectsExtensionArithmeticConstantsAndFunctors()
    {
        var engine = StrictEngine();

        Assert.Equal(
            RunResult.Success,
            engine.RunGoal(
                "catch((_ is e), error(type_error(evaluable, e/0), _), true), "
                    + "catch((_ is integer(1.0)), error(type_error(evaluable, integer/1), _), true)",
                out IReadOnlyList<DotProlog.Syntax.Diagnostic> diagnostics
            )
        );
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void StrictPartThreeTreatsSoftCutAsANonTerminal()
    {
        var engine = StrictEngine();
        LoadResult loaded = engine.ConsultText(
            """
            '*->'(_, _) --> [].
            rule --> ({true} *-> {true}).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("phrase(rule, [])", out _));
    }

    [Fact]
    public void StrictPartThreeTreatsRunTimeSoftCutAsANonTerminal()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output };
        LoadResult loaded = engine.ConsultText(
            """
            '*->'(_, _, S, S).
            :- initialization(( Body = (left *-> right), phrase(Body, []), write(yes) )).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("yes", output.ToString());
    }

    [Fact]
    public void StrictHostBindingRejectsAPredefinedExtension()
    {
        var engine = StrictEngine();
        var host = new PrologHost(engine.Machine);

        PrologException exception = Assert.Throws<PrologException>(() => host.Bind("member", 2));

        Assert.Contains(
            "permission_error(access, implementation_specific_feature, member/2)",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    private static PrologEngine StrictEngine() => new(PrologLanguageMode.StrictIso);
}
