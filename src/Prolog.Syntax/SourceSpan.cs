namespace Prolog.Syntax;

/// <summary>A half-open range of characters in a Prolog source file, with the 1-based position of its start.</summary>
/// <param name="Start">Zero-based offset of the first character.</param>
/// <param name="Length">Number of characters covered.</param>
/// <param name="Line">One-based line of <paramref name="Start"/>.</param>
/// <param name="Column">One-based column of <paramref name="Start"/>.</param>
public readonly record struct SourceSpan(int Start, int Length, int Line, int Column)
{
    /// <summary>An empty span at the beginning of a file, used when no better position is known.</summary>
    public static SourceSpan None => new(0, 0, 1, 1);

    /// <summary>Returns a span covering this span through the end of <paramref name="other"/>.</summary>
    public SourceSpan To(SourceSpan other) => new(Start, Math.Max(Length, other.Start + other.Length - Start), Line, Column);

    /// <inheritdoc />
    public override string ToString() => $"({Line},{Column})";
}
