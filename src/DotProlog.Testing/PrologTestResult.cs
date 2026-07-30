namespace DotProlog.Testing;

/// <summary>What happened when a test predicate ran.</summary>
/// <param name="Succeeded">Whether the predicate was proved.</param>
/// <param name="Message">Why it failed, or <see langword="null"/> when it passed.</param>
/// <param name="Output">Anything the test wrote, which is reported alongside a failure.</param>
/// <param name="Error">Anything the test wrote to <c>user_error</c>, reported the same way.</param>
public sealed record PrologTestResult(bool Succeeded, string? Message, string Output, string Error)
{
    /// <summary>The test was proved.</summary>
    public static PrologTestResult Passed(string output) => new(true, null, output, string.Empty);

    /// <summary>The test failed, threw, halted, or timed out.</summary>
    public static PrologTestResult Failed(string message, string output, string error) => new(false, message, output, error);
}
