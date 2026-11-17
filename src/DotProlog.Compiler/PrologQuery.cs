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
/// A machine runs one goal at a time. Nested calls while an enumeration is active throw
/// InvalidOperationException. Finish or dispose the enumeration before reusing the engine.
/// Do not use an engine from more than one thread.
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
        var machine = _engine.Machine;
        machine.AcquireHostCall();
        try
        {
            var result = machine.RunHost(_address);
            while (result == RunResult.Success)
            {
                yield return Capture(machine);
                result = machine.RedoHost();
            }
        }
        finally
        {
            machine.ReleaseHostCall();
        }
    }

    /// <summary>Proves the goal once and reports whether it succeeded.</summary>
    public bool Prove()
    {
        var machine = _engine.Machine;
        machine.AcquireHostCall();
        try
        {
            return machine.RunHost(_address) == RunResult.Success;
        }
        finally
        {
            machine.ReleaseHostCall();
        }
    }

    /// <summary>Returns the first answer, or <see langword="null"/> when the goal fails.</summary>
    public PrologSolution? FirstOrDefault()
    {
        var machine = _engine.Machine;
        machine.AcquireHostCall();
        try
        {
            return machine.RunHost(_address) == RunResult.Success ? Capture(machine) : null;
        }
        finally
        {
            machine.ReleaseHostCall();
        }
    }

    private PrologSolution Capture(Machine machine)
    {
        Dictionary<string, PrologValue> bindings = new(_variableNames.Length, StringComparer.Ordinal);

        if (_variableNames.Length > 0)
        {
            var holder = machine.Dereference(machine.QueryBindings);
            for (var i = 0; i < _variableNames.Length; i++)
            {
                bindings[_variableNames[i]] = PrologValue.FromTerm(machine, machine.HeapAt(holder.Index + 1 + i));
            }
        }

        return new PrologSolution(bindings);
    }
}
