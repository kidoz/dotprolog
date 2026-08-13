using System.Numerics;

namespace DotProlog.Syntax;

/// <summary>
/// A rational literal such as <c>1r3</c>, carried unreduced; conversion to a runtime term
/// canonicalizes it.
/// </summary>
/// <param name="Numerator">The numerator, carrying the sign.</param>
/// <param name="Denominator">The denominator, always positive in source text.</param>
/// <param name="Span">Source location.</param>
public sealed record RationalTerm(BigInteger Numerator, BigInteger Denominator, SourceSpan Span) : SyntaxTerm(Span);
