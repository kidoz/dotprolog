using Prolog.Runtime;

namespace Prolog.Syntax;

/// <summary>
/// Reads Prolog source into <see cref="SyntaxTerm"/>s using an operator-precedence parser driven by
/// an <see cref="OperatorTable"/>. Errors are reported as diagnostics; the reader recovers by
/// skipping to the next clause terminator so a single bad clause does not hide the rest of the file.
/// </summary>
public sealed class TermReader
{
    /// <summary>Maximum priority of a whole term.</summary>
    private const int MaxTermPriority = 1200;

    /// <summary>Maximum priority of an argument or list element; <c>,</c> separates instead of operating.</summary>
    private const int ArgumentPriority = 999;

    private readonly Lexer _lexer;
    private readonly OperatorTable _operators;
    private readonly List<Diagnostic> _diagnostics;
    private readonly string? _fileName;
    private Token _current;
    private Token? _lookahead;

    private TermReader(string text, string? fileName, OperatorTable operators, List<Diagnostic> diagnostics)
    {
        _fileName = fileName;
        _operators = operators;
        _diagnostics = diagnostics;
        _lexer = new Lexer(text, fileName, diagnostics);
        _current = _lexer.Next();
    }

    /// <summary>Reads every clause and directive in <paramref name="text"/>.</summary>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    /// <param name="operators">Operator table to read with; a default ISO table is used when omitted.</param>
    public static ParseResult ReadProgram(string text, string? fileName = null, OperatorTable? operators = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<Diagnostic> diagnostics = [];
        var reader = new TermReader(text, fileName, operators ?? new OperatorTable(), diagnostics);
        List<SyntaxTerm> clauses = [];

        while (reader._current.Kind != TokenKind.Eof)
        {
            SyntaxTerm? clause = reader.ReadClause();
            if (clause is not null)
            {
                clauses.Add(clause);
            }
        }

        return new ParseResult(clauses, diagnostics);
    }

    /// <summary>Reads a single term, which may but need not be followed by a clause terminator.</summary>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    /// <param name="operators">Operator table to read with; a default ISO table is used when omitted.</param>
    public static ParseResult ReadTerm(string text, string? fileName = null, OperatorTable? operators = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<Diagnostic> diagnostics = [];
        var reader = new TermReader(text, fileName, operators ?? new OperatorTable(), diagnostics);
        SyntaxTerm? term = reader.ParseTerm(MaxTermPriority, out _);
        if (term is not null && reader._current.Kind == TokenKind.End)
        {
            reader.Advance();
        }

        return new ParseResult(term is null ? [] : [term], diagnostics);
    }

    private SyntaxTerm? ReadClause()
    {
        SyntaxTerm? term = ParseTerm(MaxTermPriority, out _);

        if (term is null)
        {
            SkipToClauseEnd();
            return null;
        }

        if (_current.Kind != TokenKind.End)
        {
            Report(
                DiagnosticIds.MissingEndToken,
                $"Expected '.' to end the clause but found {Describe(_current)}.",
                _current.Span
            );
            SkipToClauseEnd();
            return null;
        }

        Advance();
        return term;
    }

    private void SkipToClauseEnd()
    {
        while (_current.Kind is not (TokenKind.End or TokenKind.Eof))
        {
            Advance();
        }

        if (_current.Kind == TokenKind.End)
        {
            Advance();
        }
    }

    private SyntaxTerm? ParseTerm(int maxPriority, out int priority)
    {
        SyntaxTerm? left = ParsePrimary(maxPriority, out priority);
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            string? name = InfixName(_current);
            if (name is null || !_operators.TryGetInfixOrPostfix(name, out PrologOperator op))
            {
                return left;
            }

            if (op.Priority > maxPriority || priority > op.LeftPriority)
            {
                return left;
            }

            SourceSpan operatorSpan = _current.Span;
            Advance();

            if (op.IsPostfix)
            {
                left = new CompoundTerm(name, [left], left.Span.To(operatorSpan));
                priority = op.Priority;
                continue;
            }

            SyntaxTerm? right = ParseTerm(op.RightPriority, out _);
            if (right is null)
            {
                return null;
            }

            // '|' used as an infix operator at priority 1100 denotes disjunction.
            string functor = name == "|" && op.Priority == 1100 ? ";" : name;
            left = new CompoundTerm(functor, [left, right], left.Span.To(right.Span));
            priority = op.Priority;
        }
    }

    private static string? InfixName(Token token)
    {
        if (token.Kind == TokenKind.Atom && !token.Quoted)
        {
            return token.Text;
        }

        return token.IsPunctuation(",") || token.IsPunctuation("|") ? token.Text : null;
    }

    private SyntaxTerm? ParsePrimary(int maxPriority, out int priority)
    {
        priority = 0;
        Token token = _current;

        switch (token.Kind)
        {
            case TokenKind.Integer:
                Advance();
                return new IntegerTerm(token.Integer, token.Span);

            case TokenKind.Float:
                Advance();
                return new FloatTerm(token.Float, token.Span);

            case TokenKind.String:
                Advance();
                return new StringTerm(token.Text, token.Span);

            case TokenKind.Variable:
                Advance();
                return new VariableTerm(token.Text, token.Span);

            case TokenKind.Atom:
                return ParseAtomOrOperator(maxPriority, out priority);

            case TokenKind.Punctuation when token.Text == "(":
            {
                Advance();
                SyntaxTerm? inner = ParseTerm(MaxTermPriority, out _);
                return inner is null || !Expect(")") ? null : inner;
            }

            case TokenKind.Punctuation when token.Text == "[":
                return ParseList();

            case TokenKind.Punctuation when token.Text == "{":
            {
                Advance();
                if (_current.IsPunctuation("}"))
                {
                    SourceSpan braceSpan = token.Span.To(_current.Span);
                    Advance();
                    return new AtomTerm("{}", braceSpan);
                }

                SyntaxTerm? inner = ParseTerm(MaxTermPriority, out _);
                if (inner is null)
                {
                    return null;
                }

                SourceSpan span = token.Span.To(_current.Span);
                return Expect("}") ? new CompoundTerm("{}", [inner], span) : null;
            }

            default:
                Report(DiagnosticIds.UnexpectedToken, $"Expected a term but found {Describe(token)}.", token.Span);
                return null;
        }
    }

    private SyntaxTerm? ParseAtomOrOperator(int maxPriority, out int priority)
    {
        priority = 0;
        Token token = _current;
        string name = token.Text;

        // 'foo(' with no intervening layout is a compound term; 'foo (' is not.
        Token next = Peek();
        if (next.IsPunctuation("(") && !next.PrecededByLayout)
        {
            Advance();
            Advance();
            return ParseArguments(name, token.Span);
        }

        // A sign directly in front of a numeric literal is part of the literal, not a prefix operator.
        if (!token.Quoted && name is "-" or "+" && next.Kind is TokenKind.Integer or TokenKind.Float && !next.PrecededByLayout)
        {
            Advance();
            Token literal = _current;
            Advance();
            SourceSpan span = token.Span.To(literal.Span);
            bool negate = name == "-";
            return literal.Kind == TokenKind.Integer
                ? new IntegerTerm(negate ? -literal.Integer : literal.Integer, span)
                : new FloatTerm(negate ? -literal.Float : literal.Float, span);
        }

        if (
            !token.Quoted
            && _operators.TryGetPrefix(name, out PrologOperator prefix)
            && prefix.Priority <= maxPriority
            && CanStartTerm(next)
        )
        {
            Advance();
            SyntaxTerm? operand = ParseTerm(prefix.RightPriority, out _);
            if (operand is null)
            {
                return null;
            }

            priority = prefix.Priority;
            return new CompoundTerm(name, [operand], token.Span.To(operand.Span));
        }

        Advance();
        if (!token.Quoted)
        {
            priority = _operators.MaxPriority(name);
        }

        return new AtomTerm(name, token.Span);
    }

    private bool CanStartTerm(Token token)
    {
        if (token.Kind is TokenKind.End or TokenKind.Eof)
        {
            return false;
        }

        if (token.Kind == TokenKind.Punctuation)
        {
            return token.Text is "(" or "[" or "{";
        }

        // An unquoted infix-only operator cannot start a term: in 'X = = 1' the second '=' is an error,
        // but in '- - 1' the inner '-' is a prefix operator and may.
        if (
            token.Kind == TokenKind.Atom
            && !token.Quoted
            && _operators.TryGetInfixOrPostfix(token.Text, out _)
            && !_operators.TryGetPrefix(token.Text, out _)
        )
        {
            return false;
        }

        return true;
    }

    private CompoundTerm? ParseArguments(string name, SourceSpan nameSpan)
    {
        List<SyntaxTerm> arguments = [];
        while (true)
        {
            SyntaxTerm? argument = ParseTerm(ArgumentPriority, out _);
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);

            if (_current.IsPunctuation(","))
            {
                Advance();
                continue;
            }

            SourceSpan span = nameSpan.To(_current.Span);
            return Expect(")") ? new CompoundTerm(name, arguments, span) : null;
        }
    }

    private SyntaxTerm? ParseList()
    {
        SourceSpan openSpan = _current.Span;
        Advance();

        // '[]' is lexed as a single atom, so a ']' here means the source wrote '[ ]'.
        if (_current.IsPunctuation("]"))
        {
            SourceSpan emptySpan = openSpan.To(_current.Span);
            Advance();
            return new AtomTerm("[]", emptySpan);
        }

        List<SyntaxTerm> elements = [];
        SyntaxTerm tail = new AtomTerm("[]", openSpan);

        while (true)
        {
            SyntaxTerm? element = ParseTerm(ArgumentPriority, out _);
            if (element is null)
            {
                return null;
            }

            elements.Add(element);

            if (_current.IsPunctuation(","))
            {
                Advance();
                continue;
            }

            if (_current.IsPunctuation("|"))
            {
                Advance();
                SyntaxTerm? rest = ParseTerm(ArgumentPriority, out _);
                if (rest is null)
                {
                    return null;
                }

                tail = rest;
            }

            break;
        }

        SourceSpan listSpan = openSpan.To(_current.Span);
        if (!Expect("]"))
        {
            return null;
        }

        SyntaxTerm result = tail;
        for (int i = elements.Count - 1; i >= 0; i--)
        {
            result = new CompoundTerm(ListFunctor, [elements[i], result], listSpan);
        }

        return result;
    }

    /// <summary>The ISO list constructor. Lists are right-nested <c>'.'(Head, Tail)</c> terms ending in <c>[]</c>.</summary>
    public const string ListFunctor = ".";

    /// <summary>The ISO empty list atom.</summary>
    public const string EmptyListAtom = "[]";

    private bool Expect(string punctuation)
    {
        if (_current.IsPunctuation(punctuation))
        {
            Advance();
            return true;
        }

        Report(DiagnosticIds.UnexpectedToken, $"Expected '{punctuation}' but found {Describe(_current)}.", _current.Span);
        return false;
    }

    private static string Describe(Token token) =>
        token.Kind switch
        {
            TokenKind.Eof => "end of file",
            TokenKind.End => "'.'",
            _ => $"'{token.Text}'",
        };

    private Token Peek()
    {
        _lookahead ??= _lexer.Next();
        return _lookahead.Value;
    }

    private void Advance()
    {
        if (_lookahead is { } pending)
        {
            _current = pending;
            _lookahead = null;
            return;
        }

        _current = _lexer.Next();
    }

    private void Report(string id, string message, SourceSpan span) =>
        _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, _fileName));
}
