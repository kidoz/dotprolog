namespace DotProlog.Runtime;

/// <summary>
/// A Prolog exception in flight. Ordinary failure and backtracking never use exceptions; this type
/// carries a thrown ball to the nearest <c>catch/3</c>, or out to the host when nothing catches it.
/// </summary>
/// <remarks>
/// The ball is held as a detached <see cref="TermBuffer"/> rather than a heap cell, because unwinding
/// truncates the heap the term was built on. <see cref="Exception.Message"/> is a readable rendering
/// for the host; Prolog code sees the term, which is what <c>catch/3</c> unifies against.
/// </remarks>
public sealed class PrologException : Exception
{
    internal PrologException(string message, TermBuffer ball, int ballRoot)
        : base(message)
    {
        Ball = ball;
        BallRoot = ballRoot;
    }

    /// <summary>Creates an exception with the given message and no Prolog ball.</summary>
    public PrologException(string message)
        : base(message) { }

    /// <summary>Creates an exception with the given message and cause.</summary>
    public PrologException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates an exception with no message.</summary>
    public PrologException() { }

    /// <summary>The thrown term, detached from the heap, or <see langword="null"/> for a host fault.</summary>
    internal TermBuffer? Ball { get; }

    /// <summary>Slot of the ball's root cell inside <see cref="Ball"/>.</summary>
    internal int BallRoot { get; }

    /// <summary>Whether this exception carries a Prolog term that <c>catch/3</c> could unify against.</summary>
    public bool HasBall => Ball is not null;
}
