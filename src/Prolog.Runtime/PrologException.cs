namespace Prolog.Runtime;

/// <summary>
/// A Prolog-level error such as <c>existence_error</c>. Ordinary failure and backtracking never use
/// exceptions; this type exists only for conditions that <c>catch/3</c> will handle once it lands,
/// and for host faults surfaced to the embedder.
/// </summary>
public sealed class PrologException : Exception
{
    /// <summary>Creates an exception with the given message.</summary>
    public PrologException(string message)
        : base(message) { }

    /// <summary>Creates an exception with the given message and cause.</summary>
    public PrologException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates an exception with no message.</summary>
    public PrologException() { }
}
