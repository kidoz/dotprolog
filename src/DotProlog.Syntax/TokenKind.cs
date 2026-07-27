namespace DotProlog.Syntax;

/// <summary>The lexical categories produced by <see cref="Lexer"/>.</summary>
internal enum TokenKind
{
    /// <summary>An atom: unquoted name, symbolic sequence, quoted atom, <c>!</c>, <c>;</c>, or <c>[]</c>/<c>{}</c>.</summary>
    Atom,

    /// <summary>A variable name, including the anonymous <c>_</c>.</summary>
    Variable,

    /// <summary>An integer literal.</summary>
    Integer,

    /// <summary>A floating-point literal.</summary>
    Float,

    /// <summary>A double-quoted literal.</summary>
    String,

    /// <summary>Structural punctuation: <c>( ) [ ] { } , |</c>.</summary>
    Punctuation,

    /// <summary>The clause terminator <c>.</c> followed by layout or end of input.</summary>
    End,

    /// <summary>End of input.</summary>
    Eof,
}
