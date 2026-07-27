namespace DotProlog.Syntax;

/// <summary>
/// Finds where one clause ends in a run of text, so that a term can be read from a stream without
/// reading the whole stream first.
/// </summary>
/// <remarks>
/// This runs the lexer rather than looking for a full stop, because a full stop is only a terminator
/// in some positions: not inside a quoted atom or a comment, not in <c>3.14</c>, and not as part of
/// a longer symbolic atom such as <c>=..</c>. Deciding that again by hand would be a second copy of
/// the lexical rules, and the two would drift.
/// </remarks>
public static class ClauseScanner
{
    /// <summary>
    /// The offset just past the first clause terminator in <paramref name="text"/>, or -1 when the
    /// text holds no complete clause and more input is needed.
    /// </summary>
    public static int FindClauseEnd(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<Diagnostic> ignored = [];
        var lexer = new Lexer(text, null, ignored);

        while (true)
        {
            Token token = lexer.Next();

            switch (token.Kind)
            {
                case TokenKind.End:
                    return token.Span.Start + token.Span.Length;

                case TokenKind.Eof:
                    return -1;

                default:
                    break;
            }
        }
    }

    /// <summary>Whether <paramref name="text"/> holds nothing but layout and comments.</summary>
    /// <remarks>
    /// What is left in the buffer at end of input decides between <c>end_of_file</c> and a syntax
    /// error, and trailing whitespace or a comment must not be mistaken for a partial clause.
    /// </remarks>
    public static bool IsBlank(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<Diagnostic> ignored = [];
        var lexer = new Lexer(text, null, ignored);
        return lexer.Next().Kind == TokenKind.Eof && ignored.Count == 0;
    }
}
