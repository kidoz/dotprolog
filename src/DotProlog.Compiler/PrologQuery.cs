using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>
/// A compiled goal that a host can ask for answers, one at a time.
/// </summary>
/// <remarks>
/// <para>
/// The goal is compiled once and can be run again; each call to <see cref="Solutions"/> starts it
/// from the beginning. Solutions are produced lazily — the engine is resumed only when the consumer
/// asks for the next one — so an infinite goal is fine as long as the consumer stops taking.
/// </para>
/// <para>
/// A machine runs one goal at a time. Do not interleave two enumerations from the same engine, and do
/// not use an engine from more than one thread.
/// </para>
/// </remarks>
public sealed class PrologQuery
{
    private readonly PrologEngine _engine;
    private readonly int _address;
    private readonly string[] _variableNames;

    internal PrologQuery(PrologEngine engine, int address, string[] variableNames)
    {
        _engine = engine;
        _address = address;
        _variableNames = variableNames;
    }

    /// <summary>The query's variables, in the order they appear in the goal.</summary>
    public IReadOnlyList<string> VariableNames => _variableNames;

    /// <summary>Enumerates every answer, resuming the engine on each step.</summary>
    public IEnumerable<PrologSolution> Solutions()
    {
        Machine machine = _engine.Machine;
        RunResult result = machine.Run(_address);

        while (result == RunResult.Success)
        {
            yield return Capture(machine);
            result = machine.Redo();
        }
    }

    /// <summary>Proves the goal once and reports whether it succeeded.</summary>
    public bool Prove() => _engine.Machine.Run(_address) == RunResult.Success;

    /// <summary>Returns the first answer, or <see langword="null"/> when the goal fails.</summary>
    public PrologSolution? FirstOrDefault()
    {
        Machine machine = _engine.Machine;
        return machine.Run(_address) == RunResult.Success ? Capture(machine) : null;
    }

    private PrologSolution Capture(Machine machine)
    {
        Dictionary<string, PrologValue> bindings = new(_variableNames.Length, StringComparer.Ordinal);

        if (_variableNames.Length > 0)
        {
            Cell holder = machine.Dereference(machine.QueryBindings);
            for (var i = 0; i < _variableNames.Length; i++)
            {
                bindings[_variableNames[i]] = PrologValue.FromTerm(machine, machine.HeapAt(holder.Index + 1 + i));
            }
        }

        return new PrologSolution(bindings);
    }
}
