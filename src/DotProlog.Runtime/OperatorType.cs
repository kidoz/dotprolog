namespace DotProlog.Runtime;

/// <summary>The ISO operator specifiers.</summary>
public enum OperatorType
{
    /// <summary>Prefix, non-associative argument.</summary>
    Fx,

    /// <summary>Prefix, argument may have the operator's own priority.</summary>
    Fy,

    /// <summary>Infix, both arguments below the operator's priority.</summary>
    Xfx,

    /// <summary>Infix, right-associative.</summary>
    Xfy,

    /// <summary>Infix, left-associative.</summary>
    Yfx,

    /// <summary>Postfix, argument below the operator's priority.</summary>
    Xf,

    /// <summary>Postfix, argument may have the operator's own priority.</summary>
    Yf,
}
