using Prolog.Runtime;
using Prolog.Syntax;

namespace Prolog.Compiler;

/// <summary>
/// Lowers read clauses into a <see cref="BytecodeProgram"/>: groups clauses by predicate, chains the
/// alternatives with try/retry/trust, and compiles directives into anonymous goal blocks.
/// </summary>
/// <remarks>
/// Directives are collected while reading and run after the whole unit is compiled, not at the point
/// they appear. That differs from consulting a file clause by clause, and only matters for a
/// directive that depends on clauses defined earlier in the same file being callable before later
/// ones are read.
/// </remarks>
public sealed class ProgramLoader
{
    private readonly BytecodeProgram _program;
    private readonly ConstantPool _constants;
    private readonly Machine? _machine;

    /// <summary>Creates a loader that appends to <paramref name="program"/>.</summary>
    /// <param name="program">The program to load into.</param>
    /// <param name="machine">
    /// Machine used to build the clause terms a dynamic predicate needs for <c>retract/1</c>. Without
    /// one, a <c>:- dynamic</c> declaration is reported rather than honoured.
    /// </param>
    public ProgramLoader(BytecodeProgram program, Machine? machine = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        _program = program;
        _constants = new ConstantPool(program);
        _machine = machine;
    }

    /// <summary>Lowers <paramref name="clauses"/> into the program.</summary>
    /// <param name="clauses">Clauses and directives in source order.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    public LoadResult Load(IReadOnlyList<SyntaxTerm> clauses, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        List<Diagnostic> diagnostics = [];
        List<int> directives = [];
        List<int> initialization = [];
        List<int> predicateOrder = [];
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> predicates = [];

        HashSet<int> dynamicPredicates = [];

        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
                // A dynamic declaration changes how later clauses are stored, so it is honoured while
                // reading rather than queued like an ordinary directive.
                if (directive.Arguments[0] is CompoundTerm { Name: "dynamic", Arity: 1 } declaration)
                {
                    DeclareDynamic(declaration.Arguments[0], dynamicPredicates, diagnostics, fileName);
                    continue;
                }

                CompileDirective(directive.Arguments[0], diagnostics, directives, initialization, fileName);
                continue;
            }

            SyntaxTerm head = clause;
            SyntaxTerm? body = null;
            if (clause is CompoundTerm { Name: ":-", Arity: 2 } rule)
            {
                head = rule.Arguments[0];
                body = rule.Arguments[1];
            }

            if (!TryGetHeadFunctor(head, out int functorId))
            {
                diagnostics.Add(
                    new Diagnostic(
                        CompilerDiagnosticIds.InvalidClauseHead,
                        DiagnosticSeverity.Error,
                        "A clause head must be an atom or a compound term.",
                        head.Span,
                        fileName
                    )
                );
                continue;
            }

            if (!predicates.TryGetValue(functorId, out List<(SyntaxTerm, SyntaxTerm?)>? bucket))
            {
                bucket = [];
                predicates[functorId] = bucket;
                predicateOrder.Add(functorId);
            }

            bucket.Add((head, body));
        }

        foreach (int functorId in predicateOrder)
        {
            if (dynamicPredicates.Contains(functorId))
            {
                EmitDynamicClauses(functorId, predicates[functorId], diagnostics, fileName);
                continue;
            }

            EmitPredicate(functorId, predicates[functorId], diagnostics, fileName);
        }

        return new LoadResult(diagnostics, directives, initialization);
    }

    /// <summary>Handles <c>:- dynamic Name/Arity</c>, a comma sequence of them, or a list of them.</summary>
    private void DeclareDynamic(SyntaxTerm indicators, HashSet<int> declared, List<Diagnostic> diagnostics, string? fileName)
    {
        List<SyntaxTerm> pending = [indicators];

        while (pending.Count > 0)
        {
            SyntaxTerm term = pending[^1];
            pending.RemoveAt(pending.Count - 1);

            switch (term)
            {
                case CompoundTerm { Name: ",", Arity: 2 } or CompoundTerm { Name: ".", Arity: 2 }:
                    pending.AddRange(((CompoundTerm)term).Arguments);
                    continue;

                case AtomTerm { Name: "[]" }:
                    continue;

                case CompoundTerm { Name: "/", Arity: 2 } indicator
                    when indicator.Arguments[0] is AtomTerm name && indicator.Arguments[1] is IntegerTerm arity:
                {
                    if (_machine is null)
                    {
                        Report(
                            diagnostics,
                            CompilerDiagnosticIds.DynamicNotAvailable,
                            "A dynamic declaration needs a machine to load into.",
                            term.Span,
                            fileName
                        );
                        continue;
                    }

                    int functorId = _program.Symbols.InternFunctor(name.Name, (int)arity.Value);
                    _program.DeclareDynamic(functorId);
                    declared.Add(functorId);
                    continue;
                }

                default:
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidDynamicDeclaration,
                        "Expected a predicate indicator of the form Name/Arity.",
                        term.Span,
                        fileName
                    );
                    continue;
            }
        }
    }

    /// <summary>Compiles each clause of a dynamic predicate separately and adds it to the database.</summary>
    private void EmitDynamicClauses(
        int functorId,
        List<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        DynamicPredicate predicate = _program.DeclareDynamic(functorId);
        Machine machine = _machine!;
        int rule = _program.Symbols.InternFunctor(":-", 2);

        foreach ((SyntaxTerm head, SyntaxTerm? body) in clauses)
        {
            var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
            int address = compiler.Compile(head, body);
            if (address < 0)
            {
                continue;
            }

            // retract/1 matches against the clause as a term, so keep a detached copy of it.
            Dictionary<string, Cell> variables = [];
            Cell headCell = TermReifier.ToHeap(machine, head, variables);
            Cell bodyCell = body is null ? Cell.Atom(machine.Symbols.True) : TermReifier.ToHeap(machine, body, variables);

            var term = new TermBuffer();
            int root = term.Copy(machine, machine.CreateStructure(rule, [headCell, bodyCell]));

            predicate.Append(
                new DynamicClause
                {
                    CodeAddress = address,
                    Term = term,
                    TermRoot = root,
                    Birth = _program.Generation,
                }
            );
        }
    }

    private static void Report(List<Diagnostic> diagnostics, string id, string message, SourceSpan span, string? fileName) =>
        diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName));

    private void CompileDirective(
        SyntaxTerm goal,
        List<Diagnostic> diagnostics,
        List<int> directives,
        List<int> initialization,
        string? fileName
    )
    {
        bool deferred = goal is CompoundTerm { Name: "initialization", Arity: 1 };
        SyntaxTerm actual = deferred ? ((CompoundTerm)goal).Arguments[0] : goal;

        var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
        int address = compiler.Compile(new AtomTerm("$directive", actual.Span), actual);
        if (address < 0)
        {
            return;
        }

        (deferred ? initialization : directives).Add(address);
    }

    private void EmitPredicate(
        int functorId,
        List<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        int entry = _program.CodeLength;
        int pendingAlternative = -1;

        for (int i = 0; i < clauses.Count; i++)
        {
            if (clauses.Count > 1)
            {
                if (i == 0)
                {
                    pendingAlternative = _program.Emit(OpCode.TryMeElse, 0) + 1;
                }
                else if (i < clauses.Count - 1)
                {
                    _program.Patch(pendingAlternative, _program.CodeLength);
                    pendingAlternative = _program.Emit(OpCode.RetryMeElse, 0) + 1;
                }
                else
                {
                    _program.Patch(pendingAlternative, _program.CodeLength);
                    _program.Emit(OpCode.TrustMe);
                }
            }

            var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
            compiler.Compile(clauses[i].Head, clauses[i].Body);
        }

        _program.DefinePredicate(functorId, entry);
    }

    private bool TryGetHeadFunctor(SyntaxTerm head, out int functorId)
    {
        switch (head)
        {
            case AtomTerm atom:
                functorId = _program.Symbols.InternFunctor(atom.Name, 0);
                return true;

            case CompoundTerm compound:
                functorId = _program.Symbols.InternFunctor(compound.Name, compound.Arity);
                return true;

            default:
                functorId = -1;
                return false;
        }
    }
}
