namespace DotProlog.Testing;

/// <summary>What happened when a test predicate ran.</summary>
/// <param name="Succeeded">Whether the predicate was proved.</param>
/// <param name="Message">Why it failed, or <see langword="null"/> when it passed.</param>
/// <param name="Output">Anything the test wrote, which is reported alongside a failure.</param>
public sealed record PrologTestResult(bool Succeeded, string? Message, string Output)
{
    /// <summary>The test was proved.</summary>
    public static PrologTestResult Passed(string output) => new(true, null, output);

    /// <summary>The test failed, threw, or halted.</summary>
    public static PrologTestResult Failed(string message, string output) => new(false, message, output);
}
