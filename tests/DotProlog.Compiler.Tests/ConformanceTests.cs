using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Runs the conformance cases in <c>tests/conformance/iso_conformance.pl</c>.
/// </summary>
/// <remarks>
/// <para>
/// The cases are encoded from ISO/IEC 13211-1 clause 8 and are our own work, not a third-party
/// suite: Ulrich Neumerkel's conformity tables carry no licence, and Logtalk's suite is written for
/// its own test framework. So this measures the engine against the standard as we read it, which is
/// worth more than nothing and less than external verification. <c>COMPATIBILITY.md</c> says so.
/// </para>
/// </remarks>
public sealed class ConformanceTests
{
    [Fact]
    public void EveryConformanceCaseBehavesAsTheStandardSays()
    {
        string suite = Path.Combine(RepositoryRoot(), "tests", "conformance", "iso_conformance.pl");
        Assert.True(File.Exists(suite), $"The conformance suite is missing from {suite}.");

        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultFile(suite);
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal(RunResult.Success, engine.RunGoal("run_conformance", out _));

        string[] lines = output.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string[] failures = [.. lines.Where(line => line.StartsWith("FAIL ", StringComparison.Ordinal))];
        int cases = lines
            .Where(line => line.StartsWith("cases ", StringComparison.Ordinal))
            .Select(line => int.Parse(line["cases ".Length..], System.Globalization.CultureInfo.InvariantCulture))
            .Single();

        Assert.Equal(cases, lines.Count(line => line.StartsWith("PASS ", StringComparison.Ordinal)) + failures.Length);
        Assert.True(failures.Length == 0, $"Conformance regressed:\n{string.Join("\n", failures)}");
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
}
