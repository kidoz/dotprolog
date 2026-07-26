namespace Prolog.CodeGen.CSharp;

/// <summary>The term shapes a contract can name.</summary>
public enum ContractTypeKind
{
    /// <summary>An atom, mapped to <see cref="string"/>.</summary>
    Atom,

    /// <summary>An integer, mapped to <see cref="long"/>.</summary>
    Integer,

    /// <summary>A float, mapped to <see cref="double"/>.</summary>
    Float,

    /// <summary>Any term, mapped to <c>PrologValue</c>.</summary>
    Term,

    /// <summary>A list, mapped to <see cref="IReadOnlyList{T}"/>.</summary>
    List,
}
