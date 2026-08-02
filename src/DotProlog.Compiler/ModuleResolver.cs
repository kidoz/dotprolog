using DotProlog.Syntax;
using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>
/// Rewrites a clause so that each name in it says which module's predicate it means.
/// </summary>
/// <remarks>
/// <para>
/// A predicate defined in module <c>m</c> is compiled under the name <c>m:p</c>, and a call inside
/// <c>m</c> to something <c>m</c> defines is rewritten to that name. A call to anything else — a
/// builtin, a library predicate, something in <c>user</c> — is left alone, so the fallback is always
/// the global name.
/// </para>
/// <para>
/// A goal passed as data rather than called directly is treated differently. Where the argument is
/// called as it stands it is resolved like any other goal, but a closure that <c>call/N</c> will
/// extend is wrapped as <c>m:Closure</c>, because how many arguments it ends up with is only known
/// when it is called. That wrapping is what <c>'$qualify'/3</c> unwinds at run time.
/// </para>
/// </remarks>
internal sealed class ModuleResolver
{
    private readonly ModuleTable _modules;
    private readonly string _module;
    private readonly HashSet<PredicateIndicator> _local;
    private readonly BytecodeProgram? _program;
    private readonly bool _isoContext;

    /// <summary>Creates a resolver for clauses being loaded into <paramref name="module"/>.</summary>
    /// <param name="modules">The program's module table.</param>
    /// <param name="module">The module being loaded into.</param>
    /// <param name="local">Every predicate the unit being loaded defines.</param>
    /// <param name="program">Program used to distinguish predefined global procedures from unknown local ones.</param>
    /// <param name="isoContext">Whether the source is an ISO module body, including the default <c>user</c> module.</param>
    internal ModuleResolver(
        ModuleTable modules,
        string module,
        HashSet<PredicateIndicator> local,
        BytecodeProgram? program = null,
        bool isoContext = false
    )
    {
        _modules = modules;
        _module = module;
        _local = local;
        _program = program;
        _isoContext = isoContext
            || (modules.Catalog.TryGet(module, out ModuleDefinition? definition) && definition!.InterfacePrepared);
    }

    /// <summary>Whether this resolver has anything to do.</summary>
    internal bool IsIdentity => _module == ModuleTable.UserModule && _local.Count == 0;

    /// <summary>Rewrites a clause head to the name its predicate is compiled under.</summary>
    internal SyntaxTerm ResolveHead(SyntaxTerm head) =>
        head switch
        {
            AtomTerm atom => new AtomTerm(ModuleTable.QualifiedName(_module, atom.Name), atom.Span),
            CompoundTerm compound => new CompoundTerm(
                ModuleTable.QualifiedName(_module, compound.Name),
                compound.Arguments,
                compound.Span
            ),
            _ => head,
        };

    /// <summary>Rewrites a goal, and every goal reachable from it, to the predicates they mean.</summary>
    internal SyntaxTerm ResolveGoal(SyntaxTerm goal)
    {
        return goal switch
        {
            // A variable goal is only known at run time, so it carries its module and is resolved then.
            VariableTerm => Qualify(goal),
            AtomTerm atom => atom.Name is "!" or "true" or "fail" or "false" ? atom
            : Rename(atom.Name, 0) is { } renamed ? new AtomTerm(renamed, atom.Span)
            : atom,
            CompoundTerm compound => ResolveCompound(compound),
            _ => goal,
        };
    }

    private CompoundTerm ResolveCompound(CompoundTerm compound)
    {
        if (_isoContext && compound is { Name: "predicate_property", Arity: 2 })
        {
            return new CompoundTerm(
                "$predicate_property",
                [new AtomTerm(_module, compound.Span), compound.Arguments[0], compound.Arguments[1]],
                compound.Span
            );
        }

        if (_isoContext && compound is { Name: "current_predicate", Arity: 1 })
        {
            return new CompoundTerm(
                "$current_predicate",
                [new AtomTerm(_module, compound.Span), compound.Arguments[0]],
                compound.Span
            );
        }

        string? contextual = _isoContext ? (compound.Name, compound.Arity) switch
        {
            ("op", 3) => "$op",
            ("current_op", 3) => "$current_op",
            ("char_conversion", 2) => "$char_conversion",
            ("current_char_conversion", 2) => "$current_char_conversion",
            ("set_prolog_flag", 2) => "$set_prolog_flag",
            ("current_prolog_flag", 2) => "$current_prolog_flag",
            ("asserta", 1) => "$asserta",
            ("assertz", 1) => "$assertz",
            ("retract", 1) => "$retract",
            ("clause", 2) => "$clause",
            ("abolish", 1) => "$abolish",
            ("write", 1 or 2) => "$write",
            ("writeq", 1 or 2) => "$writeq",
            ("write_term", 2 or 3) => "$write_term",
            ("read", 1 or 2) => "$read",
            ("read_term", 2 or 3) => "$read_term",
            _ => null,
        } : null;
        if (contextual is not null)
        {
            return new CompoundTerm(
                contextual,
                [new AtomTerm(_module, compound.Span), .. compound.Arguments],
                compound.Span
            );
        }

        switch (compound)
        {
            // Control constructs are transparent: what matters is the goals inside them.
            case { Name: "," or ";" or "->" or "*->", Arity: 2 }:
                return compound with { Arguments = [ResolveGoal(compound.Arguments[0]), ResolveGoal(compound.Arguments[1])] };

            case { Name: "\\+", Arity: 1 }:
                return compound with { Arguments = [ResolveGoal(compound.Arguments[0])] };

            // ^/2 qualifies a bagof/3 goal; the goal inside it still has to be resolved.
            case { Name: "^", Arity: 2 }:
                return compound with { Arguments = [compound.Arguments[0], ResolveGoal(compound.Arguments[1])] };

            // An explicit qualification is left for run time, since the module named may not be
            // loaded yet and the answer must not depend on load order.
            case { Name: ":", Arity: 2 }:
                return compound;

            default:
                break;
        }

        var indicator = new PredicateIndicator(compound.Name, compound.Arity);
        var meta = _modules.MetaArgumentsOf(_module, indicator);
        IReadOnlyList<SyntaxTerm> arguments = meta is null ? compound.Arguments : ResolveMetaArguments(compound, meta);

        return Rename(compound.Name, compound.Arity) is { } renamed
            ? new CompoundTerm(renamed, arguments, compound.Span)
            : compound with
            {
                Arguments = arguments,
            };
    }

    private SyntaxTerm[] ResolveMetaArguments(CompoundTerm compound, int[] meta)
    {
        var arguments = new SyntaxTerm[compound.Arity];

        for (var i = 0; i < compound.Arity; i++)
        {
            arguments[i] = meta[i] switch
            {
                ModuleTable.ClauseArgument => ResolveClause(compound.Arguments[i]),
                ModuleTable.HeadArgument => ResolveLocalHead(compound.Arguments[i]),
                < 0 => compound.Arguments[i],
                0 => ResolveGoal(compound.Arguments[i]),
                _ => Qualify(compound.Arguments[i]),
            };
        }

        return arguments;
    }

    /// <summary>Rewrites a clause handed to <c>assertz/1</c> and its like.</summary>
    private SyntaxTerm ResolveClause(SyntaxTerm clause) =>
        clause is CompoundTerm { Name: ":-", Arity: 2 } rule
            ? rule with
            {
                Arguments = [ResolveLocalHead(rule.Arguments[0]), ResolveGoal(rule.Arguments[1])],
            }
            : ResolveLocalHead(clause);

    /// <summary>
    /// Rewrites a clause head that appears as an argument. Unlike the head of a clause being loaded,
    /// this one is only renamed when the module actually has that predicate: asserting to something
    /// it does not define means the global one.
    /// </summary>
    private SyntaxTerm ResolveLocalHead(SyntaxTerm head) =>
        head switch
        {
            AtomTerm atom when Rename(atom.Name, 0) is { } renamed => new AtomTerm(renamed, atom.Span),
            CompoundTerm compound when Rename(compound.Name, compound.Arity) is { } renamed => new CompoundTerm(
                renamed,
                compound.Arguments,
                compound.Span
            ),
            _ => head,
        };

    /// <summary>
    /// The name a call should compile to, or <see langword="null"/> to leave it as written.
    /// </summary>
    private string? Rename(string name, int arity)
    {
        var indicator = new PredicateIndicator(name, arity);

        // What this unit defines wins over anything imported, which is what makes a module's own
        // helper reachable even when a name it imports would otherwise shadow it.
        if (_local.Contains(indicator) || _modules.Defines(_module, indicator))
        {
            return ModuleTable.QualifiedName(_module, name);
        }

        var from = _modules.DefiningModuleOf(_module, indicator);
        if (from is not null)
        {
            return ModuleTable.QualifiedName(from, name);
        }

        if (
            _module != ModuleTable.UserModule
            && _modules.Catalog.TryGet(_module, out ModuleDefinition? definition)
            && definition!.InterfacePrepared
            && !IsGlobalProcedure(name, arity)
        )
        {
            return ModuleTable.QualifiedName(_module, name);
        }

        return null;
    }

    private bool IsGlobalProcedure(string name, int arity)
    {
        if (_program is null)
        {
            return false;
        }

        if (name == "call" && arity is >= 1 and <= 8)
        {
            return true;
        }

        var functor = _program.Symbols.InternFunctor(name, arity);
        return _program.Builtins.TryGetId(functor, out _)
            || (_program.IsDefined(functor) && !_program.IsUserPredicate(functor));
    }

    /// <summary>Wraps a term as <c>Module:Term</c>, for something resolved when it is called.</summary>
    private SyntaxTerm Qualify(SyntaxTerm term) =>
        _module == ModuleTable.UserModule ? term : new CompoundTerm(":", [new AtomTerm(_module, term.Span), term], term.Span);
}
