namespace DotProlog.Runtime;

/// <summary>
/// Calls Prolog predicates with .NET values and reads the results back.
/// </summary>
/// <remarks>
/// <para>
/// This is the surface a generated facade sits on. It deliberately says nothing about determinism:
/// the caller picks <see cref="Prove(PrologPredicate, PrologInput[])"/>, <see cref="CallOnce(PrologPredicate, PrologInput[])"/>, or <see cref="CallAll"/> according to
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
    public bool Prove(PrologPredicate predicate, params PrologInput[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return Prove(predicate, arguments.AsSpan());
    }

    /// <summary>Proves a predicate using span inputs without allocating argument arrays.</summary>
    public bool Prove(PrologPredicate predicate, params ReadOnlySpan<PrologInput> arguments)
    {
        ValidateArguments(predicate, arguments.Length);
        Span<Cell> cells = stackalloc Cell[arguments.Length];
        return Start(predicate, arguments, cells) == RunResult.Success;
    }

    /// <summary>
    /// Calls the predicate and returns the values its output arguments took, or
    /// <see langword="null"/> if it failed. Later solutions are discarded.
    /// </summary>
    public PrologValue[]? CallOnce(PrologPredicate predicate, params PrologInput[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return CallOnce(predicate, arguments.AsSpan());
    }

    /// <summary>Calls once with span inputs, allocating only the detached output values.</summary>
    public PrologValue[]? CallOnce(PrologPredicate predicate, params ReadOnlySpan<PrologInput> arguments)
    {
        ValidateArguments(predicate, arguments.Length);
        Span<Cell> cells = stackalloc Cell[arguments.Length];
        RunResult result = Start(predicate, arguments, cells);
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

        ValidateArguments(predicate, arguments.Length);
        cells = new Cell[arguments.Length];
        return Start(predicate, arguments, cells);
    }

    private void ValidateArguments(PrologPredicate predicate, int count)
    {
        if (count != predicate.Arity)
        {
            throw new PrologException($"{predicate} takes {predicate.Arity} arguments, but {count} were given.");
        }
        if (count >= Machine.ArgumentRegisterCount)
            throw PrologErrors.Representation(_machine, "max_arity");
    }

    private RunResult Start(PrologPredicate predicate, ReadOnlySpan<PrologInput> arguments, Span<Cell> cells)
    {
        // Reset before building arguments, which may refer to the cleared heap.
        _machine.BeginCall();
        for (var i = 0; i < arguments.Length; i++)
        {
            cells[i] = arguments[i].Build(_machine);
        }
        return _machine.Call(predicate.FunctorId, cells);
    }

    private PrologValue[] ReadOutputs(ReadOnlySpan<PrologInput> arguments, ReadOnlySpan<Cell> cells)
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
