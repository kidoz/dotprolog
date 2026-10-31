namespace DotProlog.Packages;

/// <summary>Evaluates a SPDX license expression against an exact allow list.</summary>
internal static class PackageLicenseExpression
{
    public static bool Allows(string expression, IReadOnlySet<string> allowed)
    {
        var parser = new Parser(expression);
        var node = parser.ParseOr();
        if (!parser.Done)
            throw new InvalidDataException($"Invalid license expression '{expression}'.");
        return node.IsAllowed(allowed);
    }

    private abstract record Node
    {
        public abstract bool IsAllowed(IReadOnlySet<string> allowed);
    }

    private sealed record Identifier(string Name) : Node
    {
        public override bool IsAllowed(IReadOnlySet<string> allowed) => allowed.Contains(Name);
    }

    private sealed record Conjunction(Node Left, Node Right) : Node
    {
        public override bool IsAllowed(IReadOnlySet<string> allowed) => Left.IsAllowed(allowed) && Right.IsAllowed(allowed);
    }

    private sealed record Disjunction(Node Left, Node Right) : Node
    {
        public override bool IsAllowed(IReadOnlySet<string> allowed) => Left.IsAllowed(allowed) || Right.IsAllowed(allowed);
    }

    private sealed class Parser(string expression)
    {
        private int _index;

        public bool Done => Skip() == expression.Length;

        public Node ParseOr()
        {
            var left = ParseAnd();
            while (Take("OR"))
                left = new Disjunction(left, ParseAnd());
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseFactor();
            while (Take("AND"))
                left = new Conjunction(left, ParseFactor());
            return left;
        }

        private Node ParseFactor()
        {
            if (Take("("))
            {
                var inner = ParseOr();
                if (!Take(")"))
                    throw Error();
                return inner;
            }
            var name = ReadIdentifier();
            if (Take("WITH"))
                name = name + " WITH " + ReadIdentifier();
            return new Identifier(name);
        }

        private bool Take(string token)
        {
            var index = Skip();
            if (token is "(" or ")")
            {
                if (index >= expression.Length || expression[index] != token[0])
                    return false;
                _index = index + 1;
                return true;
            }
            if (
                index + token.Length > expression.Length
                || !expression.AsSpan(index, token.Length).SequenceEqual(token)
                || (index + token.Length < expression.Length && !char.IsWhiteSpace(expression[index + token.Length]))
            )
                return false;
            _index = index + token.Length;
            return true;
        }

        private string ReadIdentifier()
        {
            var index = Skip();
            var end = index;
            while (end < expression.Length && IsIdentifier(expression[end]))
                end++;
            if (end == index)
                throw Error();
            _index = end;
            return expression[index..end];
        }

        private static bool IsIdentifier(char value) =>
            value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '+';

        private int Skip()
        {
            while (_index < expression.Length && char.IsWhiteSpace(expression[_index]))
                _index++;
            return _index;
        }

        private InvalidDataException Error() => new($"Invalid license expression '{expression}'.");
    }
}
