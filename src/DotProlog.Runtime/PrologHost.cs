namespace DotProlog.Runtime;

/// <summary>
/// Calls Prolog predicates with .NET values and reads the results back.
/// </summary>
/// <remarks>
/// <para>
/// This is the surface a generated facade sits on. It deliberately says nothing about determinism:
/// the caller picks <see cref="Prove"/>, <see cref="CallOnce"/>, or <see cref="CallAll"/> according to
/// the contract, and the facade's signature follows from that choice.
/// </para>
/// <para>
/// A host wraps one machine, so it runs one call at a time and is not thread-safe.
/// </para>
/// </remarks>
public sealed class PrologHost
{
    private readonly Machine _machine;

    /// <summary>Creates a host over <paramref name="machine"/>.</summary>
    public PrologHost(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        _machine = machine;
    }

    /// <summary>The machine this host calls into.</summary>
    public Machine Machine => _machine;

    /// <summary>Resolves <c>name/arity</c> once, so that repeated calls cost no lookup.</summary>
    /// <exception cref="PrologException">
    /// No such predicate is defined and the <c>unknown</c> flag is <c>error</c>.
    /// </exception>
    public PrologPredicate Bind(string name, int arity)
    {
        ArgumentNullException.ThrowIfNull(name);

        var functorId = _machine.Symbols.InternFunctor(name, arity);
        if (_machine.Program.IsStrictIsoExtension(functorId))
        {
            throw PrologErrors.Permission(_machine, "access", "implementation_specific_feature", functorId);
        }

        if (!_machine.Program.IsDefined(functorId) && _machine.Program.Flags.Unknown == UnknownProcedureAction.Error)
        {
            throw PrologErrors.UndefinedProcedure(_machine, functorId);
        }

        return new PrologPredicate(functorId, name, arity);
    }

    /// <summary>Proves the predicate once and reports whether it succeeded, ignoring any outputs.</summary>
    public bool Prove(PrologPredicate predicate, params PrologInput[] arguments) =>
        Start(predicate, arguments, out _) == RunResult.Success;

    /// <summary>
    /// Calls the predicate and returns the values its output arguments took, or
    /// <see langword="null"/> if it failed. Later solutions are discarded.
    /// </summary>
    public PrologValue[]? CallOnce(PrologPredicate predicate, params PrologInput[] arguments)
    {
        RunResult result = Start(predicate, arguments, out Cell[] cells);
        return result == RunResult.Success ? ReadOutputs(arguments, cells) : null;
    }

    /// <summary>
    /// Calls the predicate and yields the output values of every solution, resuming the engine only
    /// as the consumer asks for the next one.
    /// </summary>
    public IEnumerable<PrologValue[]> CallAll(PrologPredicate predicate, params PrologInput[] arguments)
    {
        RunResult result = Start(predicate, arguments, out Cell[] cells);

        while (result == RunResult.Success)
        {
            yield return ReadOutputs(arguments, cells);
            result = _machine.Redo();
        }
    }

    private RunResult Start(PrologPredicate predicate, PrologInput[] arguments, out Cell[] cells)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Length != predicate.Arity)
        {
            throw new PrologException($"{predicate} takes {predicate.Arity} arguments, but {arguments.Length} were given.");
        }

        // The reset comes first, then the arguments are built on the cleared heap.
        _machine.BeginCall();

        cells = new Cell[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            cells[i] = arguments[i].Build(_machine);
        }

        return _machine.Call(predicate.FunctorId, cells);
    }

    private PrologValue[] ReadOutputs(PrologInput[] arguments, Cell[] cells)
    {
        var count = 0;
        foreach (PrologInput argument in arguments)
        {
            if (argument.IsOutput)
            {
                count++;
            }
        }

        var outputs = new PrologValue[count];
        var next = 0;

        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].IsOutput)
            {
                outputs[next++] = PrologValue.FromTerm(_machine, cells[i]);
            }
        }

        return outputs;
    }
}
