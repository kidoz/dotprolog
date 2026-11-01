using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologAllSolutionsLinterTests
{
    [Fact]
    public void ReportsAnExistentialVariableMissingFromTheGoal() =>
        Assert.Contains(Lint("run(Xs) :- bagof(X, Y^member(X, [1]), Xs)."), Id(LintDiagnosticIds.UnusedExistentialVariable));

    [Fact]
    public void AcceptsAnExistentialVariableUsedByTheGoal() =>
        Assert.DoesNotContain(
            Lint("run(Xs) :- bagof(X, Y^member(X-Y, [1-a]), Xs)."),
            Id(LintDiagnosticIds.UnusedExistentialVariable)
        );

    [Fact]
    public void ReportsAnAllSolutionsTemplateWithNoSharedVariable() =>
        Assert.Contains(Lint("run(Xs) :- findall(X, member(Y, [1]), Xs)."), Id(LintDiagnosticIds.UnsharedAllSolutionsTemplate));

    [Fact]
    public void AcceptsSharedTemplateVariablesAndGroundCalls()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("run(Xs, Ys) :- findall(X, member(X, [1]), Xs), findall(a, true, Ys).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UnsharedAllSolutionsTemplate));
    }

    [Fact]
    public void ReportsAGoalSingletonThatIsUsedElsewhere() =>
        Assert.Contains(Lint("run(Y, Z) :- bagof(X, member(Y, [1]), Z), write(Y)."), Id(LintDiagnosticIds.AllSolutionsSingleton));

    [Fact]
    public void DoesNotDuplicateAnOrdinarySingletonWarning() =>
        Assert.DoesNotContain(Lint("run(Z) :- bagof(X, member(Y, [1]), Z)."), Id(LintDiagnosticIds.AllSolutionsSingleton));

    [Fact]
    public void ReportsFindallUsedOnlyForFailureDrivenBacktracking() =>
        Assert.Contains(Lint("run :- findall(X, member(X, [1]), _), fail."), Id(LintDiagnosticIds.FindallBacktracking));

    [Fact]
    public void AcceptsFindallWhoseResultIsUsed() =>
        Assert.DoesNotContain(
            Lint("run(Xs) :- findall(X, member(X, [1]), Xs), length(Xs, 1)."),
            Id(LintDiagnosticIds.FindallBacktracking)
        );

    private static IReadOnlyList<Diagnostic> Lint(string source)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, "source.pl");
    }

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
