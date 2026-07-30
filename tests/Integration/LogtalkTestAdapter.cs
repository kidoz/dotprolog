namespace Integration.Tests;

/// <summary>
/// Reads ISO-prefixed lgtunit declarations while treating their goals and expectations as opaque
/// Prolog text. It adapts the wrapper, never the expected behavior.
/// </summary>
internal static class LogtalkTestAdapter
{
    /// <summary>Reads every enabled and explicitly disabled <c>iso_*</c> declaration in one source.</summary>
    internal static IReadOnlyList<LogtalkTestDeclaration> ReadDeclarations(string source, string relativePath)
    {
        var declarations = new List<LogtalkTestDeclaration>();

        foreach (string clause in SplitClauses(source))
        {
            string text = TrimLeadingTrivia(clause);
            bool disabled = text.StartsWith("- test(iso", StringComparison.Ordinal);
            int testStart =
                disabled ? 2
                : text.StartsWith("test(iso", StringComparison.Ordinal) ? 0
                : -1;

            if (testStart < 0)
            {
                continue;
            }

            string declaration = text[testStart..];
            int neck = FindTopLevel(declaration, ":-");
            if (neck < 0)
            {
                throw new InvalidDataException($"{relativePath}: test declaration has no clause body: {declaration}");
            }

            string head = declaration[..neck].Trim();
            if (!head.StartsWith("test(", StringComparison.Ordinal) || !head.EndsWith(')'))
            {
                throw new InvalidDataException($"{relativePath}: malformed test head: {head}");
            }

            List<string> arguments = SplitTopLevel(head[5..^1], ',');
            if (arguments.Count is < 1 or > 3)
            {
                throw new InvalidDataException(
                    $"{relativePath}: expected one to three test arguments but found {arguments.Count}: {head}"
                );
            }

            string id = arguments[0];
            if (!id.StartsWith("iso_", StringComparison.Ordinal))
            {
                continue;
            }

            string body = declaration[(neck + 2)..].Trim();
            if (!body.EndsWith('.'))
            {
                throw new InvalidDataException($"{relativePath}: test body has no terminating full stop: {id}");
            }

            body = body[..^1].Trim();
            if (body.Length == 0)
            {
                throw new InvalidDataException($"{relativePath}: test body is empty: {id}");
            }

            declarations.Add(
                new LogtalkTestDeclaration(
                    relativePath,
                    id,
                    arguments.Count >= 2 ? arguments[1] : "true",
                    arguments.Count == 3 ? arguments[2] : null,
                    body,
                    disabled
                )
            );
        }

        return declarations;
    }

    private static List<string> SplitClauses(string source)
    {
        var clauses = new List<string>();
        int start = 0;
        var state = new ScanState();

        for (int index = 0; index < source.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (source[index] == '.' && state.IsTopLevel && IsLayoutOrEndAfterFullStop(source, index + 1))
            {
                clauses.Add(source[start..(index + 1)]);
                start = index + 1;
            }
        }

        return clauses;
    }

    private static int FindTopLevel(string source, string token)
    {
        var state = new ScanState();

        for (int index = 0; index <= source.Length - token.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (state.IsTopLevel && source.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevel(string source, char separator)
    {
        var parts = new List<string>();
        int start = 0;
        var state = new ScanState();

        for (int index = 0; index < source.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (source[index] == separator && state.IsTopLevel)
            {
                parts.Add(source[start..index].Trim());
                start = index + 1;
            }
        }

        parts.Add(source[start..].Trim());
        return parts;
    }

    /// <summary>
    /// Advances quote, comment, and delimiter state. Returns true when the current character belongs
    /// to lexical shielding and therefore cannot be structural punctuation.
    /// </summary>
    private static bool Advance(string source, ref int index, ScanState state)
    {
        char current = source[index];
        char next = index + 1 < source.Length ? source[index + 1] : '\0';

        if (state.LineComment)
        {
            if (current == '\n')
            {
                state.LineComment = false;
            }

            return true;
        }

        if (state.BlockComment)
        {
            if (current == '*' && next == '/')
            {
                state.BlockComment = false;
                index++;
            }

            return true;
        }

        if (state.Quote != '\0')
        {
            if (state.Escaped)
            {
                state.Escaped = false;
            }
            else if (current == '\\')
            {
                state.Escaped = true;
            }
            else if (current == state.Quote)
            {
                if (next == state.Quote)
                {
                    index++;
                }
                else
                {
                    state.Quote = '\0';
                }
            }

            return true;
        }

        if (current == '%')
        {
            state.LineComment = true;
            return true;
        }

        if (current == '/' && next == '*')
        {
            state.BlockComment = true;
            index++;
            return true;
        }

        // In 0'c the apostrophe introduces a character payload; it is not an opening quote.
        if (current == '\'' && index > 0 && source[index - 1] == '0')
        {
            SkipCharacterCodePayload(source, ref index);
            return true;
        }

        if (current is '\'' or '"' or '`')
        {
            state.Quote = current;
            return true;
        }

        switch (current)
        {
            case '(':
                state.Parentheses++;
                return true;
            case ')':
                state.Parentheses--;
                return true;
            case '[':
                state.Brackets++;
                return true;
            case ']':
                state.Brackets--;
                return true;
            case '{':
                state.Braces++;
                return true;
            case '}':
                state.Braces--;
                return true;
            default:
                return false;
        }
    }

    private static void SkipCharacterCodePayload(string source, ref int apostrophe)
    {
        int payload = apostrophe + 1;
        if (payload >= source.Length)
        {
            return;
        }

        if (source[payload] != '\\')
        {
            apostrophe = payload;
            return;
        }

        int escaped = payload + 1;
        if (escaped >= source.Length)
        {
            apostrophe = payload;
            return;
        }

        if (source[escaped] is 'x' or 'o')
        {
            int closing = source.IndexOf('\\', escaped + 1);
            apostrophe = closing < 0 ? escaped : closing;
            return;
        }

        apostrophe = escaped;
    }

    private static bool IsLayoutOrEndAfterFullStop(string source, int index) =>
        index >= source.Length
        || char.IsWhiteSpace(source[index])
        || source[index] == '%'
        || (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*');

    private static string TrimLeadingTrivia(string source)
    {
        int index = 0;

        while (index < source.Length)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index < source.Length && source[index] == '%')
            {
                int newline = source.IndexOf('\n', index + 1);
                index = newline < 0 ? source.Length : newline + 1;
                continue;
            }

            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
            {
                int closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = closing < 0 ? source.Length : closing + 2;
                continue;
            }

            break;
        }

        return source[index..].Trim();
    }

    private sealed class ScanState
    {
        internal int Parentheses { get; set; }

        internal int Brackets { get; set; }

        internal int Braces { get; set; }

        internal char Quote { get; set; }

        internal bool Escaped { get; set; }

        internal bool LineComment { get; set; }

        internal bool BlockComment { get; set; }

        internal bool IsTopLevel => Parentheses == 0 && Brackets == 0 && Braces == 0;
    }
}
