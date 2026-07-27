namespace DotProlog.Runtime;

/// <summary>A single operator definition, as installed by <c>op/3</c>.</summary>
/// <param name="Priority">Priority in 1..1200; lower binds tighter.</param>
/// <param name="Type">The operator specifier.</param>
/// <param name="Name">The operator's atom.</param>
public readonly record struct PrologOperator(int Priority, OperatorType Type, string Name)
{
    /// <summary>Whether this definition is prefix.</summary>
    public bool IsPrefix => Type is OperatorType.Fx or OperatorType.Fy;

    /// <summary>Whether this definition is infix.</summary>
    public bool IsInfix => Type is OperatorType.Xfx or OperatorType.Xfy or OperatorType.Yfx;

    /// <summary>Whether this definition is postfix.</summary>
    public bool IsPostfix => Type is OperatorType.Xf or OperatorType.Yf;

    /// <summary>Highest priority allowed for the left argument.</summary>
    public int LeftPriority =>
        Type switch
        {
            OperatorType.Yfx or OperatorType.Yf => Priority,
            _ => Priority - 1,
        };

    /// <summary>Highest priority allowed for the right argument.</summary>
    public int RightPriority =>
        Type switch
        {
            OperatorType.Xfy or OperatorType.Fy => Priority,
            _ => Priority - 1,
        };
}
