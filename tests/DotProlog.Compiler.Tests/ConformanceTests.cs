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
/// <para>
/// A case that fails is listed in <see cref="KnownFailures"/> with the reason. That makes both
/// directions visible: a new failure fails this test, and fixing a known one fails it too until the
/// list is updated.
/// </para>
/// </remarks>
public sealed class ConformanceTests
{
    /// <summary>Cases known not to pass, each with why it is acceptable for now.</summary>
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal)
    {
        // call((!, fail ; true)). The suite runs every case through call/1, and a cut inside a
        // meta-called goal is local to it here, so it does not prune the disjunction's
        // alternative. Written as a clause body the same goal fails correctly, which a test in
        // ControlConstructTests pins. Closing this needs a call barrier the engine does not have.
        ["FAIL 7.8.4"] = "cut inside a meta-called goal is local; see COMPATIBILITY.md",
    };

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

        string[] unexpected = [.. failures.Where(failure => !KnownFailures.Keys.Any(failure.Contains))];
        Assert.True(unexpected.Length == 0, $"Conformance regressed:\n{string.Join("\n", unexpected)}");

        string[] fixedUp =
        [
            .. KnownFailures.Keys.Where(known => !failures.Any(failure => failure.Contains(known, StringComparison.Ordinal))),
        ];

        Assert.True(
            fixedUp.Length == 0,
            $"These no longer fail and should be removed from KnownFailures: {string.Join(", ", fixedUp)}"
        );
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
