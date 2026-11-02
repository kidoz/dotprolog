using DotProlog.Runtime;

namespace DotProlog.Syntax;

/// <summary>A tolerant lexical snapshot for word completion, including incomplete clauses.</summary>
public sealed class PrologCompletionText
{
    private readonly string _text;
    private readonly List<Token> _tokens = [];
    private readonly List<(int Start, int End)> _comments = [];

    private PrologCompletionText(string text) => _text = text;

    /// <summary>Tokenizes once without executing source or requiring a complete parse.</summary>
    public static PrologCompletionText Create(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new PrologCompletionText(text);
        var lexer = new Lexer(text, null, [], flags: new PrologFlags())
        {
            CommentRead = (start, end) => result._comments.Add((start, end)),
        };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Token token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                break;
            result._tokens.Add(token);
        }
        return result;
    }

    /// <summary>Returns a word completion site at a UTF-16 offset, or null in unsupported text.</summary>
    /// <remarks>Quoted names, operators, numbers, comments and variable module qualifiers are excluded.</remarks>
    public PrologCompletionPrefix? At(int offset)
    {
        if (offset < 0 || offset > _text.Length || _comments.Any(span => offset >= span.Start && offset <= span.End))
            return null;

        int current = -1;
        int previous = -1;
        int clauseStart = 0;
        int clauseEnd = _tokens.Count;
        for (int index = 0; index < _tokens.Count; index++)
        {
            Token token = _tokens[index];
            int end = token.Span.Start + token.Span.Length;
            if (token.Kind == TokenKind.End)
            {
                if (end <= offset)
                    clauseStart = index + 1;
                else
                {
                    clauseEnd = index;
                    break;
                }
            }
            if (token.Span.Start < offset && end >= offset)
            {
                if (IsWord(token))
                    current = index;
                else if (offset < end || token.Quoted || token.Kind is TokenKind.String or TokenKind.Integer or TokenKind.Float)
                    return null;
            }
            else if (token.Span.Start == offset && current < 0 && IsWord(token))
                current = index;
            if (end <= offset)
                previous = index;
        }

        Token? word = current >= 0 ? _tokens[current] : null;
        int start = word?.Span.Start ?? offset;
        int length = word?.Span.Length ?? 0;
        string prefix = _text[start..offset];
        bool variable = word?.Kind == TokenKind.Variable;
        int before = current >= 0 ? current - 1 : previous;
        string? module = null;
        if (before >= clauseStart && _tokens[before] is { Text: ":", Quoted: false })
        {
            if (before == clauseStart || _tokens[before - 1] is not { Kind: TokenKind.Atom } qualifier)
                return null;
            module = qualifier.Text;
            if (variable)
                return null;
        }

        string[] variables =
        [
            .. _tokens
                .Skip(clauseStart)
                .Take(clauseEnd - clauseStart)
                .Where((token, index) => token.Kind == TokenKind.Variable && token.Text != "_" && index + clauseStart != current)
                .Select(token => token.Text)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        return new PrologCompletionPrefix(prefix, new SourceSpan(start, length, 1, 1), variable, variables, module);
    }

    private static bool IsWord(Token token) =>
        !token.Quoted
        && token.Kind is TokenKind.Atom or TokenKind.Variable
        && token.Text.Length > 0
        && (token.Text[0] == '_' || CharacterClass.IsIdentifierStart(CharacterClass.At(token.Text, 0, true).Code));
}
