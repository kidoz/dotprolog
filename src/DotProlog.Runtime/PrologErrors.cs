namespace DotProlog.Runtime;

/// <summary>
/// Builds the ISO error terms the engine raises, each of the shape <c>error(Formal, Context)</c>.
/// </summary>
/// <remarks>
/// Every factory returns an exception carrying both a Prolog term, for <c>catch/3</c> to unify
/// against, and a readable message for a host that lets the error escape. The two are built
/// separately so that constructing an error never depends on the operator table: an error raised
/// while the table is being changed still has to describe itself.
/// </remarks>
public static class PrologErrors
{
    /// <summary>A term that should have been instantiated was not.</summary>
    public static PrologException Instantiation(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Build(machine, Cell.Atom(machine.Symbols.InternAtom("instantiation_error")), "instantiation_error");
    }

    /// <summary>A term was the wrong type: <c>type_error(Expected, Culprit)</c>.</summary>
    public static PrologException Type(Machine machine, string expected, Cell culprit)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Binary(machine, "type_error", expected, culprit);
    }

    /// <summary>A term was the right type but an unacceptable value: <c>domain_error(Domain, Culprit)</c>.</summary>
    public static PrologException Domain(Machine machine, string domain, Cell culprit)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Binary(machine, "domain_error", domain, culprit);
    }

    /// <summary>A predicate has no definition: <c>existence_error(procedure, Name/Arity)</c>.</summary>
    public static PrologException UndefinedProcedure(Machine machine, int functorId)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Functor functor = machine.Symbols.GetFunctor(functorId);
        Cell indicator = machine.CreateStructure(
            machine.Symbols.InternFunctor("/", 2),
            [Cell.Atom(functor.NameAtom), Cell.Integer60(functor.Arity)]
        );

        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("existence_error", 2),
            [Cell.Atom(machine.Symbols.InternAtom("procedure")), indicator]
        );

        return Build(machine, formal, $"existence_error(procedure, {machine.Symbols.DescribeFunctor(functorId)})");
    }

    /// <summary>An operation is not allowed: <c>permission_error(Operation, Type, Name/Arity)</c>.</summary>
    public static PrologException Permission(Machine machine, string operation, string type, int functorId)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Functor functor = machine.Symbols.GetFunctor(functorId);
        Cell indicator = machine.CreateStructure(
            machine.Symbols.InternFunctor("/", 2),
            [Cell.Atom(functor.NameAtom), Cell.Integer60(functor.Arity)]
        );

        return Permission(machine, operation, type, indicator);
    }

    /// <summary>An operation is not allowed: <c>permission_error(Operation, Type, Culprit)</c>.</summary>
    public static PrologException Permission(Machine machine, string operation, string type, Cell culprit)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("permission_error", 3),
            [Cell.Atom(machine.Symbols.InternAtom(operation)), Cell.Atom(machine.Symbols.InternAtom(type)), culprit]
        );

        return Build(
            machine,
            formal,
            $"permission_error({operation}, {type}, {TermWriter.ToDisplayString(machine, culprit, quoted: true)})"
        );
    }

    /// <summary>An arithmetic operation could not produce a value: <c>evaluation_error(What)</c>.</summary>
    public static PrologException Evaluation(Machine machine, string what)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Unary(machine, "evaluation_error", what);
    }

    /// <summary>An implementation limit was reached: <c>representation_error(Flag)</c>.</summary>
    public static PrologException Representation(Machine machine, string flag)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Unary(machine, "representation_error", flag);
    }

    /// <summary>The host I/O system failed while carrying out a Prolog operation.</summary>
    public static PrologException System(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return Build(machine, Cell.Atom(machine.Symbols.InternAtom("system_error")), "system_error");
    }

    /// <summary>A term is not evaluable as arithmetic: <c>type_error(evaluable, Name/Arity)</c>.</summary>
    public static PrologException NotEvaluable(Machine machine, string name, int arity)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Cell indicator = machine.CreateStructure(
            machine.Symbols.InternFunctor("/", 2),
            [Cell.Atom(machine.Symbols.InternAtom(name)), Cell.Integer60(arity)]
        );

        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("type_error", 2),
            [Cell.Atom(machine.Symbols.InternAtom("evaluable")), indicator]
        );

        return Build(machine, formal, $"type_error(evaluable, {name}/{arity})");
    }

    private static PrologException Unary(Machine machine, string kind, string argument)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor(kind, 1),
            [Cell.Atom(machine.Symbols.InternAtom(argument))]
        );

        return Build(machine, formal, $"{kind}({argument})");
    }

    private static PrologException Binary(Machine machine, string kind, string first, Cell culprit)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor(kind, 2),
            [Cell.Atom(machine.Symbols.InternAtom(first)), culprit]
        );

        return Build(machine, formal, $"{kind}({first}, {TermWriter.ToDisplayString(machine, culprit, quoted: true)})");
    }

    private static PrologException Build(Machine machine, Cell formal, string description)
    {
        Cell error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);

        return machine.CreateBall(error, description);
    }
}
