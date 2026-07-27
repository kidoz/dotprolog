namespace Prolog.Runtime;

/// <summary>
/// A loaded program: the instruction stream, the constant pool, and the entry address of every
/// defined predicate. The compiler writes into it and <see cref="Machine"/> executes it. Address
/// zero always holds <see cref="OpCode.Stop"/> so that returning from a top-level goal needs no
/// sentinel check in the dispatch loop.
/// </summary>
public sealed class BytecodeProgram
{
    /// <summary>The address of the instruction that returns control to the host.</summary>
    public const int TopLevelReturnAddress = 0;

    /// <summary>
    /// The address of a <see cref="OpCode.TrustMe"/> followed by a <see cref="OpCode.Fail"/>. A soft
    /// cut retargets a choice point here so that reaching it discards that choice point and keeps
    /// backtracking, without having to remove it from the middle of the stack. The
    /// <see cref="OpCode.TrustMe"/> is what makes this terminate: a bare failure would land on the
    /// same choice point again forever.
    /// </summary>
    public const int PopAndFailAddress = 1;

    /// <summary>The address a dynamic predicate's choice point resumes at to try its next clause.</summary>
    public const int NextClauseAddress = 3;

    /// <summary>The address a nondeterministic builtin's choice point resumes at.</summary>
    public const int RedoBuiltinAddress = 4;

    private const int Undefined = -1;

    private int[] _code = new int[1024];
    private Cell[] _constants = new Cell[64];
    private int[] _entryPoints = new int[64];
    private int _constantCount;
    private readonly Dictionary<int, DynamicPredicate> _dynamicPredicates = [];

    /// <summary>Creates an empty program with its own symbol table and builtin registry.</summary>
    public BytecodeProgram()
    {
        Symbols = new SymbolTable();
        Builtins = new BuiltinRegistry(Symbols);
        Array.Fill(_entryPoints, Undefined);
        _code[TopLevelReturnAddress] = (int)OpCode.Stop;
        _code[PopAndFailAddress] = (int)OpCode.TrustMe;
        _code[PopAndFailAddress + 1] = (int)OpCode.Fail;
        _code[NextClauseAddress] = (int)OpCode.NextClause;
        _code[RedoBuiltinAddress] = (int)OpCode.RedoBuiltin;
        CodeLength = 5;
    }

    /// <summary>The atoms, functors, and floats this program refers to.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>The native predicates this program may call.</summary>
    public BuiltinRegistry Builtins { get; }

    /// <summary>
    /// The operators in force. Reading and writing share one table, so an <c>op/3</c> run at any
    /// point changes how later text is both parsed and printed.
    /// </summary>
    public OperatorTable Operators { get; } = new();

    /// <summary>Number of instruction words emitted so far; also the address of the next emit.</summary>
    public int CodeLength { get; private set; }

    internal int[] Code => _code;

    internal Cell[] Constants => _constants;

    /// <summary>Records that <paramref name="functorId"/> is defined at <paramref name="address"/>.</summary>
    public void DefinePredicate(int functorId, int address)
    {
        EnsureEntryPoints(functorId + 1);
        _entryPoints[functorId] = address;
    }

    /// <summary>Returns the entry address of <paramref name="functorId"/>, or -1 if it is undefined.</summary>
    public int EntryPointOf(int functorId) =>
        functorId >= 0 && functorId < _entryPoints.Length ? _entryPoints[functorId] : Undefined;

    /// <summary>Whether <paramref name="functorId"/> has a definition.</summary>
    public bool IsDefined(int functorId) => EntryPointOf(functorId) != Undefined;

    /// <summary>
    /// The current clause generation. A goal snapshots this when it starts, and then sees exactly the
    /// clauses that existed at that moment — the logical update view.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>Advances the clause generation and returns the new value.</summary>
    public int NextGeneration() => ++Generation;

    /// <summary>The compiler used by <c>assertz/1</c> and <c>consult/1</c>, installed by the host.</summary>
    public IRuntimeCompiler? RuntimeCompiler { get; set; }

    /// <summary>Whether <paramref name="functorId"/> is a dynamic predicate.</summary>
    public bool IsDynamic(int functorId) => _dynamicPredicates.ContainsKey(functorId);

    /// <summary>
    /// Returns the dynamic predicate for <paramref name="functorId"/>, declaring it if necessary.
    /// Declaring emits a one-instruction trampoline and points the predicate's entry at it.
    /// </summary>
    /// <exception cref="PrologException">A static predicate of that name and arity already exists.</exception>
    internal DynamicPredicate DeclareDynamic(int functorId)
    {
        if (_dynamicPredicates.TryGetValue(functorId, out DynamicPredicate? existing))
        {
            return existing;
        }

        if (IsDefined(functorId))
        {
            throw new PrologException($"permission_error(modify, static_procedure, {Symbols.DescribeFunctor(functorId)})");
        }

        int trampoline = CodeLength;
        Emit(OpCode.EnterDynamic, functorId);

        var predicate = new DynamicPredicate { FunctorId = functorId, TrampolineAddress = trampoline };
        _dynamicPredicates[functorId] = predicate;
        DefinePredicate(functorId, trampoline);
        return predicate;
    }

    /// <summary>
    /// Makes <paramref name="alias"/> name the same predicate as <paramref name="target"/>, and
    /// reports whether it did.
    /// </summary>
    /// <remarks>
    /// A module system uses this to give an exported predicate its plain name alongside its
    /// qualified one. A dynamic predicate is aliased by sharing the clause list, not just the entry
    /// address, so that asserting through either name is seen through both.
    /// </remarks>
    public bool AliasPredicate(int alias, int target)
    {
        if (alias == target || IsDefined(alias) || IsDynamic(alias) || !(IsDefined(target) || IsDynamic(target)))
        {
            return false;
        }

        if (_dynamicPredicates.TryGetValue(target, out DynamicPredicate? dynamic))
        {
            _dynamicPredicates[alias] = dynamic;
        }

        DefinePredicate(alias, EntryPointOf(target));
        return true;
    }

    /// <summary>Returns the dynamic predicate for <paramref name="functorId"/>, or <see langword="null"/>.</summary>
    internal DynamicPredicate? FindDynamic(int functorId) =>
        _dynamicPredicates.TryGetValue(functorId, out DynamicPredicate? predicate) ? predicate : null;

    /// <summary>Appends an instruction with no operands and returns its address.</summary>
    public int Emit(OpCode opCode) => EmitWord((int)opCode);

    /// <summary>Appends an instruction with one operand and returns its address.</summary>
    public int Emit(OpCode opCode, int operand)
    {
        int address = EmitWord((int)opCode);
        EmitWord(operand);
        return address;
    }

    /// <summary>Appends an instruction with two operands and returns its address.</summary>
    public int Emit(OpCode opCode, int first, int second)
    {
        int address = EmitWord((int)opCode);
        EmitWord(first);
        EmitWord(second);
        return address;
    }

    /// <summary>Overwrites the instruction word at <paramref name="address"/>; used to patch forward jumps.</summary>
    public void Patch(int address, int value) => _code[address] = value;

    /// <summary>Adds <paramref name="constant"/> to the pool and returns its index.</summary>
    public int AddConstant(Cell constant)
    {
        if (_constantCount == _constants.Length)
        {
            Array.Resize(ref _constants, _constants.Length * 2);
        }

        _constants[_constantCount] = constant;
        return _constantCount++;
    }

    private int EmitWord(int word)
    {
        if (CodeLength == _code.Length)
        {
            Array.Resize(ref _code, _code.Length * 2);
        }

        int address = CodeLength;
        _code[address] = word;
        CodeLength = address + 1;
        return address;
    }

    private void EnsureEntryPoints(int required)
    {
        if (required <= _entryPoints.Length)
        {
            return;
        }

        int capacity = _entryPoints.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }

        int previous = _entryPoints.Length;
        Array.Resize(ref _entryPoints, capacity);
        Array.Fill(_entryPoints, Undefined, previous, capacity - previous);
    }
}
