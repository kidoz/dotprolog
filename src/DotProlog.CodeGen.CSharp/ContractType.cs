using System.Globalization;

namespace DotProlog.CodeGen.CSharp;

/// <summary>
/// A term shape named by a contract, together with how it crosses the .NET boundary.
/// </summary>
/// <param name="Kind">The shape.</param>
/// <param name="Element">The element shape, for <see cref="ContractTypeKind.List"/>.</param>
public sealed record ContractType(ContractTypeKind Kind, ContractType? Element = null)
{
    /// <summary>The C# type this maps to.</summary>
    public string ClrTypeName =>
        Kind switch
        {
            ContractTypeKind.Atom => "string",
            ContractTypeKind.Integer => "long",
            ContractTypeKind.Float => "double",
            ContractTypeKind.Term => "global::DotProlog.Runtime.PrologValue",
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"global::System.Collections.Generic.IReadOnlyList<{Element!.ClrTypeName}>"
            ),
        };

    /// <summary>An expression building a <c>PrologInput</c> from the C# expression <paramref name="value"/>.</summary>
    public string ToInputExpression(string value) =>
        Kind switch
        {
            ContractTypeKind.Atom => $"global::DotProlog.Runtime.PrologInput.Atom({value})",
            ContractTypeKind.Integer => $"global::DotProlog.Runtime.PrologInput.Integer({value})",
            ContractTypeKind.Float => $"global::DotProlog.Runtime.PrologInput.Float({value})",
            ContractTypeKind.Term => $"global::DotProlog.Runtime.PrologInput.FromValue({value})",
            _ => $"global::DotProlog.Runtime.PrologInput.List(global::System.Linq.Enumerable.ToArray("
                + $"global::System.Linq.Enumerable.Select({value}, __item => {Element!.ToInputExpression("__item")})))",
        };

    /// <summary>An expression reading a C# value out of the <c>PrologValue</c> expression <paramref name="value"/>.</summary>
    public string ToOutputExpression(string value) =>
        Kind switch
        {
            ContractTypeKind.Atom => $"global::DotProlog.Runtime.PrologMarshal.ToAtom({value})",
            ContractTypeKind.Integer => $"global::DotProlog.Runtime.PrologMarshal.ToInteger({value})",
            ContractTypeKind.Float => $"global::DotProlog.Runtime.PrologMarshal.ToFloat({value})",
            ContractTypeKind.Term => value,
            _ => $"global::DotProlog.Runtime.PrologMarshal.ToList({value}, __item => {Element!.ToOutputExpression("__item")})",
        };

    internal bool CanMarshalOutput(global::DotProlog.Runtime.PrologValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Kind switch
        {
            ContractTypeKind.Atom => value is global::DotProlog.Runtime.PrologAtom,
            ContractTypeKind.Integer => value is global::DotProlog.Runtime.PrologInteger,
            ContractTypeKind.Float => value
                is global::DotProlog.Runtime.PrologFloat
                    or global::DotProlog.Runtime.PrologInteger
                    or global::DotProlog.Runtime.PrologBigInteger
                    or global::DotProlog.Runtime.PrologRational,
            ContractTypeKind.Term => true,
            _ => value.TryGetList(out var items) && items.All(CanMarshalOutputElement),
        };
    }

    private bool CanMarshalOutputElement(global::DotProlog.Runtime.PrologValue value) => Element!.CanMarshalOutput(value);

    /// <inheritdoc />
    public override string ToString() => Kind == ContractTypeKind.List ? $"list({Element})" : Kind.ToString().ToLowerInvariant();
}
