namespace DotProlog.Packages;

/// <summary>Evaluates a SPDX license expression against an exact allow list.</summary>
internal static class PackageLicenseExpression
{
    public static bool Allows(string expression, IReadOnlySet<string> allowed)
    {
        var parser = new Parser(expression);
        return Evaluate(parser.Parse(), allowed);
    }

    private static bool Evaluate(Node root, IReadOnlySet<string> allowed)
    {
        Stack<EvaluationFrame> pending = new();
        Stack<bool> values = new();
        pending.Push(new EvaluationFrame(root, false));
        while (pending.TryPop(out var frame))
        {
            switch (frame.Node)
            {
                case Identifier identifier:
                    values.Push(allowed.Contains(identifier.Name));
                    break;
                case Conjunction conjunction when !frame.LeftEvaluated:
                    pending.Push(new EvaluationFrame(conjunction, true));
                    pending.Push(new EvaluationFrame(conjunction.Left, false));
                    break;
                case Conjunction conjunction:
                    if (values.Pop())
                        pending.Push(new EvaluationFrame(conjunction.Right, false));
                    else
                        values.Push(false);
                    break;
                case Disjunction disjunction when !frame.LeftEvaluated:
                    pending.Push(new EvaluationFrame(disjunction, true));
                    pending.Push(new EvaluationFrame(disjunction.Left, false));
                    break;
                case Disjunction disjunction:
                    if (values.Pop())
                        values.Push(true);
                    else
                        pending.Push(new EvaluationFrame(disjunction.Right, false));
                    break;
            }
        }
        return values.Pop();
    }

    private abstract record Node;

    private sealed record Identifier(string Name) : Node;

    private sealed record Conjunction(Node Left, Node Right) : Node;

    private sealed record Disjunction(Node Left, Node Right) : Node;

    private readonly record struct EvaluationFrame(Node Node, bool LeftEvaluated);

    private sealed class Parser(string expression)
    {
        private readonly Stack<Node> _nodes = new();
        private readonly Stack<Operator> _operators = new();
        private int _index;
        private bool _expectFactor = true;

        public Node Parse()
        {
            while (true)
            {
                if (_expectFactor)
                {
                    if (Take("("))
                    {
                        _operators.Push(Operator.LeftParenthesis);
                        continue;
                    }
                    var name = ReadIdentifier();
                    if (Take("WITH"))
                        name = name + " WITH " + ReadIdentifier();
                    _nodes.Push(new Identifier(name));
                    _expectFactor = false;
                    continue;
                }

                if (Skip() == expression.Length)
                    break;
                if (Take(")"))
                {
                    CloseParenthesis();
                    continue;
                }
                if (Take("AND"))
                {
                    PushOperator(Operator.And);
                    _expectFactor = true;
                    continue;
                }
                if (Take("OR"))
                {
                    PushOperator(Operator.Or);
                    _expectFactor = true;
                    continue;
                }
                throw Error();
            }

            if (_expectFactor)
                throw Error();
            while (_operators.TryPeek(out var oper))
            {
                if (oper == Operator.LeftParenthesis)
                    throw Error();
                Apply(oper);
            }
            if (_nodes.Count != 1)
                throw Error();
            return _nodes.Pop();
        }

        private void CloseParenthesis()
        {
            while (_operators.TryPeek(out var oper) && oper != Operator.LeftParenthesis)
                Apply(oper);
            if (!_operators.TryPop(out var closing) || closing != Operator.LeftParenthesis)
                throw Error();
        }

        private void PushOperator(Operator oper)
        {
            var precedence = Precedence(oper);
            while (
                _operators.TryPeek(out var previous) && previous != Operator.LeftParenthesis && Precedence(previous) >= precedence
            )
                Apply(previous);
            _operators.Push(oper);
        }

        private void Apply(Operator oper)
        {
            _operators.Pop();
            if (_nodes.Count < 2)
                throw Error();
            var right = _nodes.Pop();
            var left = _nodes.Pop();
            Node node = oper == Operator.And ? new Conjunction(left, right) : new Disjunction(left, right);
            _nodes.Push(node);
        }

        private static int Precedence(Operator oper) => oper == Operator.And ? 2 : 1;

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

    private enum Operator
    {
        LeftParenthesis,
        And,
        Or,
    }
}
