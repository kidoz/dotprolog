using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologLiteralLinterTests
{
    [Theory]
    [InlineData("run :- format(\"~z\", []).")]
    [InlineData("run :- format(\"~w~w\", [only]).")]
    [InlineData("run :- format(\"~w\", [a, b]).")]
    [InlineData("run :- format(\"~d\", [not_a_number]).")]
    [InlineData("run :- format(\"~*c\", [not_a_count, 0'x]).")]
    [InlineData("run :- format(\"~\").")]
    public void ReportsInvalidLiteralFormatCalls(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.InvalidFormatCall));

    [Theory]
    [InlineData("run :- format('~w ~a ~d ~e', [X, Y, Z, F]).")]
    [InlineData("run :- format(\"~W\", [foo, [quoted(true)]]).")]
    [InlineData("run :- format(\"~*c\", [3, 0'x]).")]
    [InlineData("run :- format(\"~@~w\", [write(x), y]).")]
    public void AcceptsValidOrDynamicFormatCalls(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.InvalidFormatCall));

    [Theory]
    [InlineData("run :- open(file, read, Stream, [bogus(true)]).")]
    [InlineData("run :- open(file, read, Stream, [type(utf8)]).")]
    [InlineData("run :- close(Stream, [force(maybe)]).")]
    [InlineData("run :- write_term(Term, [spacing(compact)]).")]
    [InlineData("run :- write_term(Term, [variable_names(not_a_list)]).")]
    public void ReportsInvalidPredicateOptions(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.InvalidPredicateOption));

    [Theory]
    [InlineData("run :- open(file, read, Stream, [alias(input), type(text), reposition(true), eof_action(error)]).")]
    [InlineData("run :- close(Stream, [force(false)]).")]
    [InlineData("run :- read_term(Term, [variable_names(Names), variables(Vars), singletons(Singletons)]).")]
    [InlineData(
        "run :- write_term(Term, [quoted(true), ignore_ops(false), numbervars(true), spacing(next_argument), variable_names(['X'=X])])."
    )]
    [InlineData("run :- write_term(Term, Options).")]
    public void AcceptsValidOrDynamicPredicateOptions(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.InvalidPredicateOption));

    [Theory]
    [InlineData("run(X) :- X is unknown(1).")]
    [InlineData("run(X) :- X is not_a_number.")]
    [InlineData("run(X) :- X is 10 // 0.")]
    [InlineData("run :- 1 =:= foo.")]
    public void ReportsInvalidArithmeticExpressions(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.InvalidArithmeticExpression));

    [Theory]
    [InlineData("run(X) :- X is 1 + 2 * 3.")]
    [InlineData("run(X) :- X is Y + Z.")]
    [InlineData("run(X) :- X is numerator(1 rdiv 2).")]
    public void AcceptsValidArithmeticExpressions(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.InvalidArithmeticExpression));

    [Fact]
    public void ReportsAPossibleBuiltinArityMismatch() =>
        Assert.Contains(Lint("run :- member(1, [1], extra)."), Id(LintDiagnosticIds.PossibleBuiltinArityMismatch));

    [Fact]
    public void AcceptsBuiltinAndFileLocalArities()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("member(_, _, _).\nrun :- member(1, [1]), member(1, [1], extra).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleBuiltinArityMismatch));
    }

    [Fact]
    public void DirectiveSignaturesAreNotReportedAsCalls() =>
        Assert.DoesNotContain(Lint(":- initialization(true)."), Id(LintDiagnosticIds.PossibleBuiltinArityMismatch));

    [Theory]
    [InlineData("run :- X == Y.")]
    [InlineData("run :- a = b.")]
    [InlineData("run :- atom(42).")]
    [InlineData("run :- var(foo).")]
    public void ReportsGoalsThatAlwaysFail(string source) => Assert.Contains(Lint(source), Id(LintDiagnosticIds.AlwaysFalseGoal));

    [Theory]
    [InlineData("run :- X = X.")]
    [InlineData("run :- a = a.")]
    [InlineData("run :- atom(foo).")]
    [InlineData("run :- ground(foo(bar)).")]
    public void ReportsGoalsWithNoUsefulEffect(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.NoEffectGoal));

    [Fact]
    public void FreshVariableUnificationIsNotReportedAsFailure()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("run :- X = Y.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.AlwaysFalseGoal));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NoEffectGoal));
    }

    [Fact]
    public void GoalShapedDataIsNotAnalyzedAsCode()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("run(result(X = X)).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NoEffectGoal));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.AlwaysFalseGoal));
    }

    private static IReadOnlyList<Diagnostic> Lint(string source)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, "source.pl");
    }

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
