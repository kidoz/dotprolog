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

    /// <summary>Creates a loader that appends to <paramref name="program"/>.</summary>
    public ProgramLoader(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        _program = program;
        _constants = new ConstantPool(program);
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

        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
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
            EmitPredicate(functorId, predicates[functorId], diagnostics, fileName);
        }

        return new LoadResult(diagnostics, directives, initialization);
    }

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
