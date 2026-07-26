namespace Prolog.Syntax;

/// <summary>
/// The operator definitions in force while reading. A table starts from the ISO default set and can
/// be extended or overridden through <see cref="Define"/>, which is what <c>op/3</c> maps onto.
/// </summary>
public sealed class OperatorTable
{
    private readonly Dictionary<string, PrologOperator> _prefix = [];
    private readonly Dictionary<string, PrologOperator> _infixOrPostfix = [];

    /// <summary>Creates a table containing the ISO default operators.</summary>
    public OperatorTable()
    {
        DefineDefaults();
    }

    /// <summary>
    /// Installs an operator. A priority of zero removes the definition in the matching class
    /// (prefix, or infix/postfix), matching <c>op/3</c>.
    /// </summary>
    public void Define(int priority, OperatorType type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(priority, 1200);

        Dictionary<string, PrologOperator> target = type is OperatorType.Fx or OperatorType.Fy ? _prefix : _infixOrPostfix;
        if (priority == 0)
        {
            target.Remove(name);
            return;
        }

        target[name] = new PrologOperator(priority, type, name);
    }

    /// <summary>Looks up a prefix definition for <paramref name="name"/>.</summary>
    public bool TryGetPrefix(string name, out PrologOperator op) => _prefix.TryGetValue(name, out op);

    /// <summary>Looks up an infix or postfix definition for <paramref name="name"/>.</summary>
    public bool TryGetInfixOrPostfix(string name, out PrologOperator op) => _infixOrPostfix.TryGetValue(name, out op);

    /// <summary>Whether <paramref name="name"/> has any operator definition.</summary>
    public bool IsOperator(string name) => _prefix.ContainsKey(name) || _infixOrPostfix.ContainsKey(name);

    /// <summary>The highest priority of any definition for <paramref name="name"/>, or zero if it is not an operator.</summary>
    public int MaxPriority(string name)
    {
        int priority = 0;
        if (_prefix.TryGetValue(name, out PrologOperator prefix))
        {
            priority = prefix.Priority;
        }

        if (_infixOrPostfix.TryGetValue(name, out PrologOperator other) && other.Priority > priority)
        {
            priority = other.Priority;
        }

        return priority;
    }

    private void DefineDefaults()
    {
        Define(1200, OperatorType.Xfx, ":-");
        Define(1200, OperatorType.Xfx, "-->");
        Define(1200, OperatorType.Fx, ":-");
        Define(1200, OperatorType.Fx, "?-");
        Define(1100, OperatorType.Xfy, ";");
        Define(1100, OperatorType.Xfy, "|");
        Define(1050, OperatorType.Xfy, "->");
        Define(1050, OperatorType.Xfy, "*->");
        Define(1000, OperatorType.Xfy, ",");
        Define(990, OperatorType.Xfx, ":=");
        Define(900, OperatorType.Fy, "\\+");

        foreach (
            string name in (string[])
                ["=", "\\=", "==", "\\==", "@<", "@>", "@=<", "@>=", "=..", "is", "=:=", "=\\=", "<", ">", "=<", ">="]
        )
        {
            Define(700, OperatorType.Xfx, name);
        }

        Define(600, OperatorType.Xfy, ":");
        foreach (string name in (string[])["+", "-", "/\\", "\\/", "xor"])
        {
            Define(500, OperatorType.Yfx, name);
        }

        foreach (string name in (string[])["*", "/", "//", "rem", "mod", "div", "<<", ">>"])
        {
            Define(400, OperatorType.Yfx, name);
        }

        Define(200, OperatorType.Xfx, "**");
        Define(200, OperatorType.Xfy, "^");
        Define(200, OperatorType.Fy, "-");
        Define(200, OperatorType.Fy, "+");
        Define(200, OperatorType.Fy, "\\");
        Define(100, OperatorType.Yfx, ".");
        Define(1, OperatorType.Fx, "$");
    }
}
