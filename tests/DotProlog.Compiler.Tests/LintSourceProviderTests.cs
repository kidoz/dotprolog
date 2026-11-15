using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class LintSourceProviderTests
{
    [Fact]
    public void CancellationBeforeLoadingDoesNotReadSources()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), "root.pl"),
                ":- include('child.pl').",
                new BytecodeProgram(),
                _ => throw new InvalidOperationException("Must not read"),
                cancellation.Token
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("child. :- include('next.pl').")]
    public void CancellationDuringIncludeReadPropagatesInsteadOfBecomingDiagnostic(string text)
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        Assert.Throws<OperationCanceledException>(() =>
            PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), "root.pl"),
                ":- include('child.pl'). root.",
                new BytecodeProgram(),
                _ =>
                {
                    reads++;
                    cancellation.Cancel();
                    return text;
                },
                cancellation.Token
            )
        );
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ProviderReceivesAbsoluteNestedPathsAndPreservesReaderStateAndOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "DotProlog-provider", "root.pl");
        List<string> reads = [];
        var loaded = PrologLintSourceLoader.Load(
            root,
            ":- include('sub/ops.pl').\nalice likes bob.",
            new BytecodeProgram(),
            path =>
            {
                reads.Add(path);
                return path.EndsWith("ops.pl", StringComparison.Ordinal)
                    ? ":- include('../declarations.pl')."
                    : ":- op(500, xfy, likes).";
            },
            TestContext.Current.CancellationToken
        );
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(root)!, "sub", "ops.pl"), reads[0]);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(root)!, "declarations.pl"), reads[1]);
        Assert.Equal(reads[1], loaded.Source.FileForClause(0));
        Assert.Equal(root, loaded.Source.FileForClause(1));
        Assert.Equal("likes", Assert.IsType<CompoundTerm>(loaded.Source.Clauses[1]).Name);
    }

    [Fact]
    public void IncludeExpansionPreservesDoubleQuotesAcrossPhysicalFileBoundaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "DotProlog-provider", "root.pl");
        var loaded = PrologLintSourceLoader.Load(
            root,
            ":- set_prolog_flag(double_quotes, codes).\n"
                + ":- include('child.pl').\n"
                + "after_include :- number_codes(_, \"2\").",
            new BytecodeProgram(),
            _ => "included :- number_codes(_, \"1\").\n:- set_prolog_flag(double_quotes, chars).",
            TestContext.Current.CancellationToken
        );

        var diagnostics = PrologLinter.AnalyzeProgram([loaded.Source]);

        var diagnostic = Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == LintDiagnosticIds.DoubleQuotedListKindMismatch
        );
        Assert.Equal(root, diagnostic.FileName);
        Assert.Contains("double_quotes=chars", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.FileName?.EndsWith("child.pl", StringComparison.Ordinal) == true
        );
    }

    [Fact]
    public void ProviderFailureBecomesResolutionDiagnosticAtIncludeSite()
    {
        var root = Path.Combine(Path.GetTempPath(), "root.pl");
        var loaded = PrologLintSourceLoader.Load(
            root,
            ":- include('missing.pl').",
            new BytecodeProgram(),
            _ => throw new FileNotFoundException("Missing overlay"),
            TestContext.Current.CancellationToken
        );
        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(LintDiagnosticIds.UnresolvedLintSource, diagnostic.Id);
        Assert.Equal(root, diagnostic.FileName);
        Assert.True(diagnostic.Span.Length > 0);
    }

    [Fact]
    public void ProviderExpansionKeepsDepthLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "root.pl");
        var count = 0;
        var loaded = PrologLintSourceLoader.Load(
            root,
            ":- include('next.pl').",
            new BytecodeProgram(),
            _ => $":- include('next{++count}.pl').",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(63, count);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == LintDiagnosticIds.UnresolvedLintSource);
    }
}
