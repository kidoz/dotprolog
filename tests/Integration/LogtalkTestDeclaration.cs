namespace Integration.Tests;

/// <summary>One ISO-prefixed test declaration read without changing its upstream expectation.</summary>
internal sealed record LogtalkTestDeclaration(
    string SourcePath,
    string Id,
    string Outcome,
    string? Options,
    string Body,
    bool Disabled,
    string? ConditionalGoal
)
{
    /// <summary>The lgtunit expectation functor, or <c>true</c> for a one-argument declaration.</summary>
    internal string OutcomeKind
    {
        get
        {
            int opening = Outcome.IndexOf('(');
            return opening < 0 ? Outcome : Outcome[..opening];
        }
    }
}
