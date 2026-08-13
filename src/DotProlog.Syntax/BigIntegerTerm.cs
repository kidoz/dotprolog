using System.Numerics;

namespace DotProlog.Syntax;

/// <summary>An integer literal outside the range of <see cref="IntegerTerm"/>.</summary>
/// <param name="Value">The value.</param>
/// <param name="Span">Source location.</param>
public sealed record BigIntegerTerm(BigInteger Value, SourceSpan Span) : SyntaxTerm(Span);
