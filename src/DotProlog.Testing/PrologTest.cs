namespace DotProlog.Testing;

/// <summary>One test predicate found in a test project.</summary>
/// <param name="Name">The predicate's name, which is also the test's display name.</param>
/// <param name="FunctorId">The predicate's functor identifier, resolved once.</param>
public readonly record struct PrologTest(string Name, int FunctorId)
{
    /// <summary>A stable identifier for the test, used by the platform to correlate runs.</summary>
    public string Uid => $"prolog:{Name}/0";
}
