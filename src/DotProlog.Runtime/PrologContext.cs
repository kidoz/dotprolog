using System.ComponentModel;
using System.Globalization;

namespace DotProlog.Runtime;

/// <summary>The execution context passed to a deterministic foreign predicate.</summary>
/// <remarks>The context and its cells are valid only for the current foreign call.</remarks>
public readonly ref struct PrologContext
{
    /// <summary>Creates a context for generated registration code.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PrologContext(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        Machine = machine;
    }

    /// <summary>The machine invoking the foreign predicate.</summary>
    public Machine Machine { get; }

    /// <summary>Formats <paramref name="term"/> using the program's current operator table.</summary>
    public string Format(Cell term)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        TermWriter.Write(Machine, term, output);
        return output.ToString();
    }
}
