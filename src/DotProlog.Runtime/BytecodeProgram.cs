namespace DotProlog.Runtime;

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
    private bool[] _userPredicates = new bool[64];
    private int _constantCount;
    private readonly Dictionary<int, DynamicPredicate> _dynamicPredicates = [];
    private readonly Dictionary<int, HashSet<int>> _staticAliases = [];
    private readonly Dictionary<int, int> _staticAliasTargets = [];
    private readonly List<(CompiledPredicateBlock Block, CompiledProgram Program)> _compiledBlocks = [];

    /// <summary>Creates an empty extended-mode program with its own symbol table and builtin registry.</summary>
    public BytecodeProgram()
        : this(PrologLanguageMode.Extended) { }

    /// <summary>Creates an empty program in <paramref name="languageMode"/>.</summary>
    public BytecodeProgram(PrologLanguageMode languageMode)
    {
        if (languageMode is not PrologLanguageMode.Extended and not PrologLanguageMode.StrictIso)
        {
            throw new ArgumentOutOfRangeException(nameof(languageMode), languageMode, "Unknown Prolog language mode.");
        }

        LanguageMode = languageMode;
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

    /// <summary>The immutable language profile selected before source preparation.</summary>
    public PrologLanguageMode LanguageMode { get; }

    /// <summary>The atoms, functors, and floats this program refers to.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>The native predicates this program may call.</summary>
    public BuiltinRegistry Builtins { get; }

    /// <summary>The ISO execution flags in force for this program.</summary>
    public PrologFlags Flags { get; } = new();

    /// <summary>The ISO input-character mappings owned by this program.</summary>
    public CharacterConversionTable CharacterConversions { get; } = new();

    /// <summary>
    /// The operators in force. Reading and writing share one table, so an <c>op/3</c> run at any
    /// point changes how later text is both parsed and printed.
    /// </summary>
    public OperatorTable Operators { get; } = new();

    /// <summary>Number of instruction words emitted so far; also the address of the next emit.</summary>
    public int CodeLength { get; private set; }

    internal int[] Code => _code;

    internal Cell[] Constants => _constants;

    /// <summary>Registers a statically generated C# block and returns its machine target.</summary>
    public int RegisterCompiledBlock(CompiledPredicateBlock block, CompiledProgram program)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(program);

        int index = _compiledBlocks.Count;
        _compiledBlocks.Add((block, program));
        return EncodeCompiledTarget(index);
    }

    internal bool ExecuteCompiled(int target, ref Machine.CompiledExecution execution)
    {
        (CompiledPredicateBlock block, CompiledProgram program) = _compiledBlocks[DecodeCompiledTarget(target)];
        return block(ref execution, program);
    }

    internal static bool IsCompiledTarget(int target) => target < Undefined;

    private static int EncodeCompiledTarget(int index) => -index - 2;

    private static int DecodeCompiledTarget(int target) => -target - 2;

    /// <summary>Records that <paramref name="functorId"/> is defined at <paramref name="address"/>.</summary>
    /// <param name="functorId">Functor identifier of the predicate.</param>
    /// <param name="address">Entry address in the instruction stream.</param>
    /// <param name="userDefined">
    /// Whether the definition came from user source. Internal bytecode predicates pass
    /// <see langword="false"/> so ISO predicate enumeration does not expose them.
    /// </param>
    public void DefinePredicate(int functorId, int address, bool userDefined = true)
    {
        if (_staticAliasTargets.Remove(functorId, out int previousTarget))
        {
            _staticAliases[previousTarget].Remove(functorId);
        }

        SetPredicate(functorId, address, userDefined);

        if (_staticAliases.TryGetValue(functorId, out HashSet<int>? aliases))
        {
            foreach (int alias in aliases)
            {
                SetPredicate(alias, address, userDefined);
            }
        }
    }

    private void SetPredicate(int functorId, int address, bool userDefined)
    {
        EnsureEntryPoints(functorId + 1);
        _entryPoints[functorId] = address;
        _userPredicates[functorId] = userDefined && !Builtins.TryGetId(functorId, out _);
    }

    /// <summary>Returns the entry address of <paramref name="functorId"/>, or -1 if it is undefined.</summary>
    public int EntryPointOf(int functorId) =>
        functorId >= 0 && functorId < _entryPoints.Length ? _entryPoints[functorId] : Undefined;

    /// <summary>Whether <paramref name="functorId"/> has a definition.</summary>
    public bool IsDefined(int functorId) => EntryPointOf(functorId) != Undefined;

    /// <summary>Whether <paramref name="functorId"/> names a currently defined user procedure.</summary>
    internal bool IsUserPredicate(int functorId) =>
        functorId >= 0
        && functorId < _userPredicates.Length
        && _userPredicates[functorId]
        && EntryPointOf(functorId) != Undefined;

    /// <summary>Whether a resolved predefined procedure is excluded by the selected strict profile.</summary>
    internal bool IsStrictIsoExtension(int functorId)
    {
        if (LanguageMode != PrologLanguageMode.StrictIso || IsUserPredicate(functorId))
        {
            return false;
        }

        Functor functor = Symbols.GetFunctor(functorId);
        if (IsoLanguageProfile.IsStandardPredicate(Symbols.AtomName(functor.NameAtom), functor.Arity))
        {
            return false;
        }

        return Builtins.TryGetId(functorId, out _) || IsDefined(functorId);
    }

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
    internal DynamicPredicate DeclareDynamic(int functorId, bool userDefined = true)
    {
        if (_dynamicPredicates.TryGetValue(functorId, out DynamicPredicate? existing))
        {
            _userPredicates[functorId] |= userDefined;
            return existing;
        }

        if (IsDefined(functorId) || Builtins.TryGetId(functorId, out _))
        {
            throw new PrologException($"permission_error(modify, static_procedure, {Symbols.DescribeFunctor(functorId)})");
        }

        int trampoline = CodeLength;
        Emit(OpCode.EnterDynamic, functorId);

        var predicate = new DynamicPredicate { FunctorId = functorId, TrampolineAddress = trampoline };
        _dynamicPredicates[functorId] = predicate;
        DefinePredicate(functorId, trampoline, userDefined);
        return predicate;
    }

    /// <summary>Declares a dynamic predicate owned by build-time-generated code.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void DeclareCompiledDynamic(int functorId) => DeclareDynamic(functorId);

    /// <summary>Adds one build-time-generated clause to a dynamic predicate.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void AddCompiledDynamicClause(int functorId, int codeTarget, Cell[] termCells, int termRoot)
    {
        ArgumentNullException.ThrowIfNull(termCells);
        DynamicPredicate predicate = DeclareDynamic(functorId);
        predicate.Append(
            new DynamicClause
            {
                CodeAddress = codeTarget,
                Term = TermBuffer.FromCells(termCells),
                TermRoot = termRoot,
                Birth = Generation,
            }
        );
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
        else
        {
            if (!_staticAliases.TryGetValue(target, out HashSet<int>? aliases))
            {
                aliases = [];
                _staticAliases[target] = aliases;
            }

            aliases.Add(alias);
            _staticAliasTargets[alias] = target;
        }

        SetPredicate(alias, EntryPointOf(target), IsUserPredicate(target));
        return true;
    }

    /// <summary>Returns the dynamic predicate for <paramref name="functorId"/>, or <see langword="null"/>.</summary>
    internal DynamicPredicate? FindDynamic(int functorId) =>
        _dynamicPredicates.TryGetValue(functorId, out DynamicPredicate? predicate) ? predicate : null;

    internal IEnumerable<KeyValuePair<int, DynamicPredicate>> DynamicPredicates => _dynamicPredicates;

    /// <summary>
    /// Removes a dynamic predicate and every alias sharing its clause database. Existing calls keep
    /// their saved clause objects, while subsequent lookups see an undefined procedure.
    /// </summary>
    internal bool AbolishDynamic(int functorId)
    {
        if (!_dynamicPredicates.TryGetValue(functorId, out DynamicPredicate? predicate))
        {
            return false;
        }

        predicate.Abolish(NextGeneration());

        List<int> aliases = [];
        foreach ((int alias, DynamicPredicate candidate) in _dynamicPredicates)
        {
            if (ReferenceEquals(candidate, predicate))
            {
                aliases.Add(alias);
            }
        }

        foreach (int alias in aliases)
        {
            _dynamicPredicates.Remove(alias);
            _entryPoints[alias] = Undefined;
            _userPredicates[alias] = false;
        }

        return true;
    }

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
        Array.Resize(ref _userPredicates, capacity);
        Array.Fill(_entryPoints, Undefined, previous, capacity - previous);
    }
}
