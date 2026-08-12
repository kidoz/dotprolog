namespace DotProlog.Runtime;

/// <summary>
/// The Prolog execution engine: heap, trail, environment stack, choice-point stack, argument
/// registers, and the instruction dispatch loop.
/// </summary>
/// <remarks>
/// The engine owns its control state. Prolog calls are jumps inside a single dispatch loop rather
/// than CLR method calls, so recursion depth is bounded by the machine's own stacks; failure is a
/// return value rather than an exception.
/// </remarks>
public sealed class Machine
{
    /// <summary>Number of argument registers, which also bounds predicate arity.</summary>
    public const int ArgumentRegisterCount = 256;

    private const int FrameHeaderSize = 4;
    private const int FrameContinuation = 0;
    private const int FrameEnvironment = 1;
    private const int FramePreviousTop = 2;
    private const int FrameCutBarrier = 3;

    private readonly BytecodeProgram _program;
    private readonly SymbolTable _symbols;
    private readonly Cell[] _x = new Cell[ArgumentRegisterCount];

    private Cell[] _heap = new Cell[1 << 16];
    private int _h;

    private int[] _trail = new int[1 << 12];
    private int _tr;

    private readonly Dictionary<int, GlobalVariable> _globals = [];
    private GlobalUndo[] _globalUndo = new GlobalUndo[8];
    private int _globalUndoTop;

    private ValueUndo[] _valueUndo = new ValueUndo[8];
    private int _valueUndoTop;

    private Cell[] _stack = new Cell[1 << 14];
    private int _stackTop;
    private int _e = -1;

    private ChoicePoint[] _choicePoints = new ChoicePoint[1 << 10];
    private int _b;
    private int _b0;

    private Cell[] _savedArguments = new Cell[1 << 12];
    private int _savedTop;

    private Cell[] _unificationStack = new Cell[256];
    private int _unificationTop;
    private readonly HashSet<ulong> _unificationVisited = [];
    private readonly HashSet<int> _callableVisited = [];

    private Cell[] _occursCheckStack = new Cell[256];
    private int _occursCheckTop;
    private int[] _occursCheckVisited = new int[1 << 16];
    private int _occursCheckEpoch;

    private int _pc;
    private int _continuation;
    private int _structureArgument;
    private bool _writeMode;
    private int _argumentCount;
    private bool _halted;
    private bool _forceTrail;
    private bool _solutionPending;
    private int _currentBuiltin = -1;
    private readonly int _callFunctor;
    private readonly List<Collection> _collections = [];
    private int _collectDepth;

    /// <summary>Creates a machine that executes <paramref name="program"/>.</summary>
    public Machine(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        _program = program;
        _symbols = program.Symbols;
        _callFunctor = _symbols.InternFunctor("call", 1);
    }

    /// <summary>The streams this program has open.</summary>
    public StreamTable Streams { get; } = new();

    /// <summary>
    /// The program's standard output, which is what an embedding host sets to capture output.
    /// </summary>
    /// <remarks>
    /// This is <c>user_output</c>, not whatever <c>set_output/1</c> last selected. A program that
    /// redirects its own output therefore cannot detach the host from the stream it handed in.
    /// </remarks>
    public TextWriter Output
    {
        get => Streams.UserOutput.Writer!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Streams.UserOutput.Writer = value;
        }
    }

    /// <summary>The program's standard error output.</summary>
    public TextWriter Error
    {
        get => Streams.UserError.Writer!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Streams.UserError.Writer = value;
        }
    }

    /// <summary>The program's standard input.</summary>
    public TextReader Input
    {
        get => Streams.UserInput.Reader!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Streams.UserInput.SetReader(value);
        }
    }

    /// <summary>Where a write with no stream argument goes.</summary>
    public TextWriter CurrentOutput => Streams.CurrentOutput.Writer!;

    /// <summary>Where a read with no stream argument comes from.</summary>
    public TextReader CurrentInput => Streams.CurrentInput.Reader!;

    /// <summary>The exit code requested by <c>halt/1</c>, or zero.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The program being executed.</summary>
    public BytecodeProgram Program => _program;

    /// <summary>Whether the dispatch loop is currently executing a Prolog goal.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The program's symbol table.</summary>
    public SymbolTable Symbols => _symbols;

    /// <summary>The operators in force, which the term writer reads and <c>op/3</c> changes.</summary>
    public OperatorTable Operators => _program.Operators;

    /// <summary>Proves the goal <c>name/arity</c>, which must be a defined predicate taking no arguments.</summary>
    /// <exception cref="PrologException">The predicate is not defined.</exception>
    public RunResult Solve(int functorId)
    {
        if (!TryEntryPoint(functorId, out var entry))
        {
            return RunResult.Failure;
        }

        return Run(entry);
    }

    /// <summary>Runs the instruction stream from <paramref name="entryAddress"/> until it stops.</summary>
    /// <exception cref="PrologException">A goal threw a ball that no <c>catch/3</c> handles.</exception>
    public RunResult Run(int entryAddress)
    {
        ResetState();

        _pc = entryAddress;
        _continuation = BytecodeProgram.TopLevelReturnAddress;
        return Dispatch();
    }

    /// <summary>
    /// Clears the machine so that argument terms can be built for a following <see cref="Call"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> resets the machine itself, which wipes the heap — so a host cannot build
    /// arguments and then run. Calling a predicate is therefore three steps: reset here, build the
    /// arguments with <see cref="CreateVariable"/> and friends, then <see cref="Call"/>.
    /// </remarks>
    public void BeginCall() => ResetState();

    /// <summary>
    /// Calls <paramref name="functorId"/> with arguments already built on the heap since
    /// <see cref="BeginCall"/>. Further solutions come from <see cref="Redo"/>.
    /// </summary>
    /// <exception cref="PrologException">The predicate is not defined, or a ball went uncaught.</exception>
    public RunResult Call(int functorId, ReadOnlySpan<Cell> arguments)
    {
        if (arguments.Length >= ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(this, "max_arity");
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            _x[i] = arguments[i];
        }

        _argumentCount = arguments.Length;
        _continuation = BytecodeProgram.TopLevelReturnAddress;
        _b0 = 0;
        if (!TryEntryPoint(functorId, out _pc))
        {
            return RunResult.Failure;
        }

        return Dispatch();
    }

    /// <summary>
    /// Asks the goal proved by the last <see cref="Run"/> or <see cref="Redo"/> for another solution.
    /// </summary>
    /// <remarks>
    /// Reaching a solution leaves the machine intact — the choice-point stack, heap, and trail are all
    /// still live — so a further answer is exactly one backtrack away. This is what lets a host
    /// enumerate solutions instead of collecting them with <c>findall/3</c> from inside Prolog.
    /// </remarks>
    /// <returns>
    /// <see cref="RunResult.Success"/> for another solution, or <see cref="RunResult.Failure"/> when
    /// the goal is exhausted or the last run did not succeed.
    /// </returns>
    public RunResult Redo()
    {
        if (!_solutionPending || !Backtrack())
        {
            _solutionPending = false;
            return RunResult.Failure;
        }

        return Dispatch();
    }

    /// <summary>Whether the goal in progress could still have another solution.</summary>
    public bool HasAlternatives => _solutionPending && _b > 0;

    /// <summary>
    /// The term holding a host query's variables, set by <c>'$bindings'/1</c>. It is built before the
    /// query's first choice point, so it survives every backtrack the query makes.
    /// </summary>
    public Cell QueryBindings { get; set; }

    private RunResult Dispatch()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A Prolog machine cannot be entered recursively.");
        }

        IsRunning = true;
        try
        {
            while (true)
            {
                try
                {
                    RunResult result = Execute();
                    _solutionPending = result == RunResult.Success;
                    return result;
                }
                catch (PrologException error) when (error.HasBall && HasCatchFrame())
                {
                    // Re-enter the dispatch loop at the recovery goal. If no catcher matches after all,
                    // UnwindToCatch reports it and the ball continues out to the host.
                    if (!UnwindToCatch(error))
                    {
                        _solutionPending = false;
                        throw;
                    }
                }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    private RunResult Execute()
    {
        // Cached for the dispatch loop. The program is append-only, so a stale array is safe for
        // every address that existed when it was cached; each place that can append — builtins,
        // meta-calls, and compiled predicates — refreshes the cache before a new address is read.
        var code = _program.Code;
        Cell[] constants = _program.Constants;

        while (true)
        {
            var proved = true;

            if (BytecodeProgram.IsCompiledTarget(_pc))
            {
                var execution = new CompiledExecution(this);
                proved = _program.ExecuteCompiled(_pc, ref execution);

                // Compiled code can consult, assert, or lower a meta-called control goal, all of
                // which append to the program and can replace these arrays — and a lowered goal's
                // entry lies beyond the old array's length.
                code = _program.Code;
                constants = _program.Constants;

                if (proved)
                {
                    continue;
                }

                if (_halted)
                {
                    return RunResult.Halted;
                }

                if (!Backtrack())
                {
                    return RunResult.Failure;
                }

                continue;
            }

            var opCode = (OpCode)code[_pc++];

            switch (opCode)
            {
                case OpCode.Stop:
                    return RunResult.Success;

                case OpCode.Allocate:
                    Allocate(code[_pc++]);
                    break;

                case OpCode.Deallocate:
                    Deallocate();
                    break;

                case OpCode.Call:
                {
                    var functorId = code[_pc++];
                    _argumentCount = code[_pc++];
                    _continuation = _pc;
                    _b0 = _b;
                    proved = TryEntryPoint(functorId, out _pc);
                    break;
                }

                case OpCode.Execute:
                {
                    var functorId = code[_pc++];
                    _argumentCount = code[_pc++];
                    _b0 = _b;
                    proved = TryEntryPoint(functorId, out _pc);
                    break;
                }

                case OpCode.CallBuiltin:
                {
                    var builtinId = code[_pc++];
                    _argumentCount = code[_pc++];
                    _currentBuiltin = builtinId;
                    proved = _program.Builtins.Implementation(builtinId)(this);

                    // assertz/1 and consult/1 append to the program, which can replace these arrays.
                    // Addresses stay valid because the program is only ever appended to.
                    code = _program.Code;
                    constants = _program.Constants;
                    break;
                }

                case OpCode.Proceed:
                    _pc = _continuation;
                    break;

                case OpCode.Cut:
                    CutTo((int)_stack[_e + FrameCutBarrier].Integer);
                    break;

                case OpCode.CutTo:
                    CutTo((int)_stack[_e + FrameHeaderSize + code[_pc++]].Integer);
                    break;

                case OpCode.SoftCut:
                {
                    var barrier = (int)_stack[_e + FrameHeaderSize + code[_pc++]].Integer;
                    if (barrier < _b)
                    {
                        _choicePoints[barrier].Alternative = BytecodeProgram.PopAndFailAddress;
                    }

                    break;
                }

                case OpCode.MarkBarrier:
                    _stack[_e + FrameHeaderSize + code[_pc++]] = Cell.Integer60(_b);
                    break;

                case OpCode.Jump:
                    _pc = code[_pc];
                    break;

                case OpCode.TryBranch:
                {
                    // A branch barrier needs no argument registers: each branch reloads its own.
                    _stack[_e + FrameHeaderSize + code[_pc++]] = Cell.Integer60(_b);
                    var savedArity = _argumentCount;
                    _argumentCount = 0;
                    PushChoicePoint(code[_pc++]);
                    _argumentCount = savedArity;
                    break;
                }

                case OpCode.MetaCall:
                    proved = MetaCall();
                    code = _program.Code;
                    constants = _program.Constants;
                    break;

                case OpCode.EnterDynamic:
                    proved = EnterDynamic(code[_pc++]);
                    break;

                case OpCode.EnterStatic:
                    proved = EnterStatic(code[_pc++]);
                    break;

                case OpCode.NextStaticClause:
                {
                    // Reached through a choice point's alternative, so that choice point is still on top.
                    ChoicePoint point = _choicePoints[_b - 1];
                    BytecodeProgram.StaticClauseIndex table = _program.StaticIndex(point.IndexTable);
                    var current = point.IndexNext;
                    var following = ClauseIndexing.NextMatch(table.Keys, current + 1, point.IndexKey);

                    if (following < 0)
                    {
                        _b--;
                        _savedTop = _choicePoints[_b].ArgumentBase;
                    }
                    else
                    {
                        _choicePoints[_b - 1].IndexNext = following;
                    }

                    _pc = table.Addresses[current];
                    break;
                }

                case OpCode.RedoBuiltin:
                {
                    // The choice point is still on top; pop it, then let the builtin offer another
                    // solution and push a fresh choice point if it has more after that.
                    ChoicePoint point = _choicePoints[_b - 1];
                    _b--;
                    _savedTop = _choicePoints[_b].ArgumentBase;
                    _choicePoints[_b].NextClause = null;

                    _currentBuiltin = point.BuiltinId;
                    _pc = point.BuiltinResume;
                    BuiltinCursor = point.NextClause;
                    proved = _program.Builtins.Retry(point.BuiltinId)(this, point.BuiltinState);

                    code = _program.Code;
                    constants = _program.Constants;
                    break;
                }

                case OpCode.NextClause:
                {
                    // Reached through a choice point's alternative, so that choice point is still on top.
                    ChoicePoint point = _choicePoints[_b - 1];
                    DynamicClause clause = point.NextClause!;
                    DynamicClause? following = DynamicPredicate.FirstVisibleMatching(
                        clause.Next,
                        point.ClauseGeneration,
                        point.IndexKey
                    );

                    if (following is null)
                    {
                        _b--;
                        _savedTop = _choicePoints[_b].ArgumentBase;
                        _choicePoints[_b].NextClause = null;
                    }
                    else
                    {
                        _choicePoints[_b - 1].NextClause = following;
                    }

                    _pc = clause.CodeAddress;
                    break;
                }

                case OpCode.PushCatch:
                {
                    var catcherSlot = code[_pc++];
                    var recovery = code[_pc++];
                    var savedArity = _argumentCount;
                    _argumentCount = 0;

                    // The frame fails through on ordinary backtracking; only a throw uses the recovery.
                    PushChoicePoint(BytecodeProgram.PopAndFailAddress);
                    _argumentCount = savedArity;

                    ref ChoicePoint frame = ref _choicePoints[_b - 1];
                    frame.CatchRecovery = recovery;
                    frame.CatcherSlot = catcherSlot;
                    frame.CatchActive = true;
                    break;
                }

                case OpCode.PopCatch:
                {
                    var index = (int)_stack[_e + FrameHeaderSize + code[_pc++]].Integer;
                    var reactivate = code[_pc++];

                    if (index >= _b)
                    {
                        break;
                    }

                    if (index == _b - 1)
                    {
                        // The goal was deterministic, so the frame is simply gone.
                        _b--;
                        _savedTop = _choicePoints[_b].ArgumentBase;
                        break;
                    }

                    // The goal left alternatives. Keep the frame for a redo, but out of scope until then.
                    _choicePoints[index].CatchActive = false;
                    var savedArity = _argumentCount;
                    _argumentCount = 0;
                    PushChoicePoint(reactivate);
                    _argumentCount = savedArity;
                    break;
                }

                case OpCode.ReactivateCatch:
                {
                    var index = (int)_stack[_e + FrameHeaderSize + code[_pc++]].Integer;
                    if (index < _b)
                    {
                        _choicePoints[index].CatchActive = true;
                    }

                    break;
                }

                case OpCode.TryMeElse:
                    PushChoicePoint(code[_pc++]);
                    break;

                case OpCode.RetryMeElse:
                    _choicePoints[_b - 1].Alternative = code[_pc++];
                    break;

                case OpCode.TrustMe:
                    _b--;
                    _savedTop = _choicePoints[_b].ArgumentBase;
                    break;

                case OpCode.GetVariable:
                {
                    var slot = code[_pc++];
                    _stack[_e + FrameHeaderSize + slot] = _x[code[_pc++]];
                    break;
                }

                case OpCode.GetValue:
                {
                    var slot = code[_pc++];
                    proved = Unify(_stack[_e + FrameHeaderSize + slot], _x[code[_pc++]]);
                    break;
                }

                case OpCode.GetConstant:
                {
                    Cell constant = constants[code[_pc++]];
                    proved = UnifyConstantWith(_x[code[_pc++]], constant);
                    break;
                }

                case OpCode.GetStructureArgument:
                {
                    var functorId = code[_pc++];
                    proved = GetStructure(functorId, _x[code[_pc++]]);
                    break;
                }

                case OpCode.GetStructureSlot:
                {
                    var functorId = code[_pc++];
                    proved = GetStructure(functorId, _stack[_e + FrameHeaderSize + code[_pc++]]);
                    break;
                }

                case OpCode.UnifyVariable:
                {
                    var slot = code[_pc++];
                    if (_writeMode)
                    {
                        _stack[_e + FrameHeaderSize + slot] = NewVariable();
                    }
                    else
                    {
                        _stack[_e + FrameHeaderSize + slot] = _heap[_structureArgument++];
                    }

                    break;
                }

                case OpCode.UnifyValue:
                {
                    var slot = code[_pc++];
                    if (_writeMode)
                    {
                        EnsureHeap(1);
                        _heap[_h++] = _stack[_e + FrameHeaderSize + slot];
                    }
                    else
                    {
                        proved = Unify(_heap[_structureArgument++], _stack[_e + FrameHeaderSize + slot]);
                    }

                    break;
                }

                case OpCode.UnifyConstant:
                {
                    Cell constant = constants[code[_pc++]];
                    if (_writeMode)
                    {
                        EnsureHeap(1);
                        _heap[_h++] = constant;
                    }
                    else
                    {
                        proved = UnifyConstantWith(_heap[_structureArgument++], constant);
                    }

                    break;
                }

                case OpCode.PutVariable:
                {
                    var slot = code[_pc++];
                    Cell variable = NewVariable();
                    _stack[_e + FrameHeaderSize + slot] = variable;
                    _x[code[_pc++]] = variable;
                    break;
                }

                case OpCode.InitVariable:
                    _stack[_e + FrameHeaderSize + code[_pc++]] = NewVariable();
                    break;

                case OpCode.PutValue:
                {
                    var slot = code[_pc++];
                    _x[code[_pc++]] = _stack[_e + FrameHeaderSize + slot];
                    break;
                }

                case OpCode.PutConstant:
                {
                    Cell constant = constants[code[_pc++]];
                    _x[code[_pc++]] = constant;
                    break;
                }

                case OpCode.PutStructureArgument:
                {
                    var functorId = code[_pc++];
                    _x[code[_pc++]] = BeginStructure(functorId);
                    break;
                }

                case OpCode.PutStructureSlot:
                {
                    var functorId = code[_pc++];
                    _stack[_e + FrameHeaderSize + code[_pc++]] = BeginStructure(functorId);
                    break;
                }

                case OpCode.Fail:
                    proved = false;
                    break;

                default:
                    throw new PrologException($"Unknown opcode {opCode} at address {_pc - 1}.");
            }

            if (proved)
            {
                continue;
            }

            if (_halted)
            {
                return RunResult.Halted;
            }

            if (!Backtrack())
            {
                return RunResult.Failure;
            }
        }
    }

    /// <summary>Returns argument register <paramref name="index"/>, dereferenced.</summary>
    public Cell Argument(int index) => Dereference(_x[index]);

    /// <summary>Replaces an argument register for an internal context-lowering builtin.</summary>
    internal void SetArgument(int index, Cell value) => _x[index] = value;

    /// <summary>Returns the heap cell at <paramref name="address"/>.</summary>
    public Cell HeapAt(int address) => _heap[address];

    /// <summary>The current trail position, for a builtin that wants to undo a trial unification.</summary>
    public int TrailMark => _tr;

    /// <summary>
    /// The choice-point stack depth. Comparing depths around a meta-call is how the library's
    /// <c>setup_call_cleanup/3</c> recognizes a deterministic exit.
    /// </summary>
    internal int ChoicePointDepth => _b;

    /// <summary>Undoes every binding trailed since <paramref name="mark"/>.</summary>
    public void UndoTo(int mark) => UndoTrail(mark);

    /// <summary>Pushes a fresh unbound variable onto the heap and returns its cell.</summary>
    public Cell CreateVariable() => NewVariable();

    /// <summary>
    /// Offers a further solution from the builtin currently executing. Call this before returning
    /// <see langword="true"/>; on backtracking the builtin's retry delegate runs with
    /// <paramref name="state"/>, and may call this again to offer another.
    /// </summary>
    /// <param name="state">Opaque value handed back to the retry delegate.</param>
    public void PushRetry(long state)
    {
        if (_currentBuiltin < 0)
        {
            throw new PrologException("PushRetry was called outside a builtin.");
        }

        var resume = _pc;
        var builtin = _currentBuiltin;

        PushChoicePoint(BytecodeProgram.RedoBuiltinAddress);

        ref ChoicePoint point = ref _choicePoints[_b - 1];
        point.BuiltinId = builtin;
        point.BuiltinResume = resume;
        point.BuiltinState = state;
    }

    /// <summary>
    /// Offers a further solution together with a clause cursor. Held as a node reference rather
    /// than a position so that <c>asserta/1</c> between solutions cannot shift the enumeration.
    /// </summary>
    internal void PushRetry(long state, DynamicClause cursor)
    {
        PushRetry(state);
        _choicePoints[_b - 1].NextClause = cursor;
    }

    /// <summary>The clause cursor saved by the choice point the current retry is resuming.</summary>
    internal DynamicClause? BuiltinCursor { get; private set; }

    /// <summary>
    /// Low-level operations used by build-time-generated C# predicate blocks.
    /// </summary>
    /// <remarks>
    /// This is a generated-code contract. Every operation mutates the same explicit state used by
    /// bytecode, so continuations and choice-point alternatives may cross either execution path.
    /// </remarks>
    public readonly ref struct CompiledExecution
    {
        private readonly Machine _machine;

        internal CompiledExecution(Machine machine) => _machine = machine;

        /// <summary>Returns successfully to the current continuation.</summary>
        public bool Stop()
        {
            _machine._pc = BytecodeProgram.TopLevelReturnAddress;
            return true;
        }

        /// <summary>Allocates an environment and continues at <paramref name="next"/>.</summary>
        public bool Allocate(int slots, int next)
        {
            _machine.Allocate(slots);
            _machine._pc = next;
            return true;
        }

        /// <summary>Deallocates the current environment and continues.</summary>
        public bool Deallocate(int next)
        {
            _machine.Deallocate();
            _machine._pc = next;
            return true;
        }

        /// <summary>Calls a predicate and saves <paramref name="next"/> as its continuation.</summary>
        public bool Call(int functorId, int arity, int next)
        {
            _machine._argumentCount = arity;
            _machine._continuation = next;
            _machine._b0 = _machine._b;
            return _machine.TryEntryPoint(functorId, out _machine._pc);
        }

        /// <summary>Tail-calls a predicate without changing the continuation.</summary>
        public bool Execute(int functorId, int arity)
        {
            _machine._argumentCount = arity;
            _machine._b0 = _machine._b;
            return _machine.TryEntryPoint(functorId, out _machine._pc);
        }

        /// <summary>Calls a native predicate and continues at <paramref name="next"/>.</summary>
        public bool CallBuiltin(int builtinId, int arity, int next)
        {
            _machine._argumentCount = arity;
            _machine._currentBuiltin = builtinId;
            _machine._pc = next;
            return _machine._program.Builtins.Implementation(builtinId)(_machine);
        }

        /// <summary>Returns to the saved continuation.</summary>
        public bool Proceed()
        {
            _machine._pc = _machine._continuation;
            return true;
        }

        /// <summary>Applies the current predicate's cut and continues.</summary>
        public bool Cut(int next)
        {
            _machine.CutTo((int)_machine._stack[_machine._e + FrameCutBarrier].Integer);
            _machine._pc = next;
            return true;
        }

        /// <summary>Creates a branch choice point and continues.</summary>
        public bool TryBranch(int slot, int alternative, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = Cell.Integer60(_machine._b);
            var savedArity = _machine._argumentCount;
            _machine._argumentCount = 0;
            _machine.PushChoicePoint(alternative);
            _machine._argumentCount = savedArity;
            _machine._pc = next;
            return true;
        }

        /// <summary>Records the current cut barrier in an environment slot.</summary>
        public bool MarkBarrier(int slot, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = Cell.Integer60(_machine._b);
            _machine._pc = next;
            return true;
        }

        /// <summary>Continues at an unconditional target.</summary>
        public bool Jump(int target)
        {
            _machine._pc = target;
            return true;
        }

        /// <summary>Cuts to the barrier stored in a slot.</summary>
        public bool CutTo(int slot, int next)
        {
            _machine.CutTo((int)_machine._stack[_machine._e + FrameHeaderSize + slot].Integer);
            _machine._pc = next;
            return true;
        }

        /// <summary>Applies soft cut to the branch stored in a slot.</summary>
        public bool SoftCut(int slot, int next)
        {
            var barrier = (int)_machine._stack[_machine._e + FrameHeaderSize + slot].Integer;
            if (barrier < _machine._b)
            {
                _machine._choicePoints[barrier].Alternative = BytecodeProgram.PopAndFailAddress;
            }

            _machine._pc = next;
            return true;
        }

        /// <summary>Calls the callable term in argument register zero.</summary>
        public bool MetaCall(int next)
        {
            _machine._pc = next;
            return _machine.MetaCall();
        }

        /// <summary>Pushes a Prolog exception frame.</summary>
        public bool PushCatch(int catcherSlot, int recovery, int next)
        {
            var savedArity = _machine._argumentCount;
            _machine._argumentCount = 0;
            _machine.PushChoicePoint(BytecodeProgram.PopAndFailAddress);
            _machine._argumentCount = savedArity;

            ref ChoicePoint frame = ref _machine._choicePoints[_machine._b - 1];
            frame.CatchRecovery = recovery;
            frame.CatcherSlot = catcherSlot;
            frame.CatchActive = true;
            _machine._pc = next;
            return true;
        }

        /// <summary>Removes or deactivates a successful Prolog exception frame.</summary>
        public bool PopCatch(int slot, int reactivate, int next)
        {
            var index = (int)_machine._stack[_machine._e + FrameHeaderSize + slot].Integer;
            if (index < _machine._b)
            {
                if (index == _machine._b - 1)
                {
                    _machine._b--;
                    _machine._savedTop = _machine._choicePoints[_machine._b].ArgumentBase;
                }
                else
                {
                    _machine._choicePoints[index].CatchActive = false;
                    var savedArity = _machine._argumentCount;
                    _machine._argumentCount = 0;
                    _machine.PushChoicePoint(reactivate);
                    _machine._argumentCount = savedArity;
                }
            }

            _machine._pc = next;
            return true;
        }

        /// <summary>Reactivates a suspended Prolog exception frame.</summary>
        public bool ReactivateCatch(int slot, int next)
        {
            var index = (int)_machine._stack[_machine._e + FrameHeaderSize + slot].Integer;
            if (index < _machine._b)
            {
                _machine._choicePoints[index].CatchActive = true;
            }

            _machine._pc = next;
            return true;
        }

        /// <summary>Pushes a clause alternative.</summary>
        public bool TryMeElse(int alternative, int next)
        {
            _machine.PushChoicePoint(alternative);
            _machine._pc = next;
            return true;
        }

        /// <summary>Retargets the current clause alternative.</summary>
        public bool RetryMeElse(int alternative, int next)
        {
            _machine._choicePoints[_machine._b - 1].Alternative = alternative;
            _machine._pc = next;
            return true;
        }

        /// <summary>Commits the final clause alternative.</summary>
        public bool TrustMe(int next)
        {
            _machine._b--;
            _machine._savedTop = _machine._choicePoints[_machine._b].ArgumentBase;
            _machine._pc = next;
            return true;
        }

        /// <summary>Copies an argument register to an environment slot.</summary>
        public bool GetVariable(int slot, int argument, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = _machine._x[argument];
            _machine._pc = next;
            return true;
        }

        /// <summary>Unifies an argument register with an environment slot.</summary>
        public bool GetValue(int slot, int argument, int next)
        {
            _machine._pc = next;
            return _machine.Unify(_machine._stack[_machine._e + FrameHeaderSize + slot], _machine._x[argument]);
        }

        /// <summary>Unifies an argument register with a constant.</summary>
        public bool GetConstant(Cell constant, int argument, int next)
        {
            _machine._pc = next;
            return _machine.UnifyConstantWith(_machine._x[argument], constant);
        }

        /// <summary>Matches or builds a structure in an argument register.</summary>
        public bool GetStructureArgument(int functorId, int argument, int next)
        {
            _machine._pc = next;
            return _machine.GetStructure(functorId, _machine._x[argument]);
        }

        /// <summary>Matches or builds a structure in an environment slot.</summary>
        public bool GetStructureSlot(int functorId, int slot, int next)
        {
            _machine._pc = next;
            return _machine.GetStructure(functorId, _machine._stack[_machine._e + FrameHeaderSize + slot]);
        }

        /// <summary>Reads or writes a structure argument into an environment slot.</summary>
        public bool UnifyVariable(int slot, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = _machine._writeMode
                ? _machine.NewVariable()
                : _machine._heap[_machine._structureArgument++];
            _machine._pc = next;
            return true;
        }

        /// <summary>Reads or writes a structure argument from an environment slot.</summary>
        public bool UnifyValue(int slot, int next)
        {
            var proved = true;
            if (_machine._writeMode)
            {
                _machine.EnsureHeap(1);
                _machine._heap[_machine._h++] = _machine._stack[_machine._e + FrameHeaderSize + slot];
            }
            else
            {
                proved = _machine.Unify(
                    _machine._heap[_machine._structureArgument++],
                    _machine._stack[_machine._e + FrameHeaderSize + slot]
                );
            }

            _machine._pc = next;
            return proved;
        }

        /// <summary>Reads or writes a constant structure argument.</summary>
        public bool UnifyConstant(Cell constant, int next)
        {
            var proved = true;
            if (_machine._writeMode)
            {
                _machine.EnsureHeap(1);
                _machine._heap[_machine._h++] = constant;
            }
            else
            {
                proved = _machine.UnifyConstantWith(_machine._heap[_machine._structureArgument++], constant);
            }

            _machine._pc = next;
            return proved;
        }

        /// <summary>Creates a fresh variable in an environment slot and argument register.</summary>
        public bool PutVariable(int slot, int argument, int next)
        {
            Cell variable = _machine.NewVariable();
            _machine._stack[_machine._e + FrameHeaderSize + slot] = variable;
            _machine._x[argument] = variable;
            _machine._pc = next;
            return true;
        }

        /// <summary>Creates a fresh variable in an environment slot.</summary>
        public bool InitVariable(int slot, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = _machine.NewVariable();
            _machine._pc = next;
            return true;
        }

        /// <summary>Copies an environment slot into an argument register.</summary>
        public bool PutValue(int slot, int argument, int next)
        {
            _machine._x[argument] = _machine._stack[_machine._e + FrameHeaderSize + slot];
            _machine._pc = next;
            return true;
        }

        /// <summary>Loads a constant into an argument register.</summary>
        public bool PutConstant(Cell constant, int argument, int next)
        {
            _machine._x[argument] = constant;
            _machine._pc = next;
            return true;
        }

        /// <summary>Begins a structure in an argument register.</summary>
        public bool PutStructureArgument(int functorId, int argument, int next)
        {
            _machine._x[argument] = _machine.BeginStructure(functorId);
            _machine._pc = next;
            return true;
        }

        /// <summary>Begins a structure in an environment slot.</summary>
        public bool PutStructureSlot(int functorId, int slot, int next)
        {
            _machine._stack[_machine._e + FrameHeaderSize + slot] = _machine.BeginStructure(functorId);
            _machine._pc = next;
            return true;
        }

        /// <summary>Enters a dynamic predicate trampoline.</summary>
        public bool EnterDynamic(int functorId)
        {
            return _machine.EnterDynamic(functorId);
        }

        /// <summary>Fails and lets the machine backtrack.</summary>
        public bool Fail()
        {
            _ = _machine;
            return false;
        }
    }

    /// <summary>
    /// Checks that a meta-called goal, and every goal reachable through the control constructs
    /// inside it, is something that could be called.
    /// </summary>
    /// <param name="goal">The sub-goal being checked.</param>
    /// <param name="whole">
    /// The goal the caller passed, which is what the error names: the standard reports the culprit
    /// as the whole term, not the part of it that turned out not to be callable.
    /// </param>
    private void RequireCallable(Cell goal, Cell whole)
    {
        goal = Dereference(goal);

        if (goal.Tag == CellTag.Reference)
        {
            // An unbound sub-goal is not an error here: it is resolved when control reaches it.
            return;
        }

        if (goal.Tag == CellTag.Atom)
        {
            return;
        }

        if (goal.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(this, "callable", whole);
        }

        var functorId = _heap[goal.Index].Index;
        Functor functor = _symbols.GetFunctor(functorId);
        var name = _symbols.AtomName(functor.NameAtom);

        var binary = functor.Arity == 2 && name is "," or ";" or "->" or "*->";
        var unary = functor.Arity == 1 && name == "\\+";

        // A rational control term revisits a construct it already validated; stop rather than
        // recurse forever. The set is cleared by the two entry points before the walk starts.
        if ((binary || unary) && !_callableVisited.Add(goal.Index))
        {
            return;
        }

        if (binary)
        {
            RequireCallable(_heap[goal.Index + 1], whole);
            RequireCallable(_heap[goal.Index + 2], whole);
        }
        else if (unary)
        {
            // \+/1 names its own argument as the culprit, where call/1 names the goal it was given.
            Cell inner = Dereference(_heap[goal.Index + 1]);
            RequireCallable(inner, inner);
        }
    }

    /// <summary>
    /// Validates a complete meta-call argument without executing it. Unlike recursive control
    /// sub-goals, an unbound top-level goal is immediately an instantiation error.
    /// </summary>
    internal void ValidateCallable(Cell goal)
    {
        goal = Dereference(goal);
        if (goal.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(this);
        }

        _callableVisited.Clear();
        RequireCallable(goal, goal);
    }

    /// <summary>Wraps <paramref name="term"/> as a thrown ball, detached from the heap.</summary>
    /// <param name="term">The term being thrown.</param>
    /// <param name="description">Readable text for a host that lets the ball escape.</param>
    public PrologException CreateBall(Cell term, string description)
    {
        var ball = new TermBuffer();
        var root = ball.Copy(this, term);
        return new PrologException(description, ball, root);
    }

    /// <summary>Starts collecting solutions, as <c>findall/3</c> does.</summary>
    internal void BeginCollect()
    {
        if (_collectDepth == _collections.Count)
        {
            _collections.Add(new Collection());
        }

        Collection collection = _collections[_collectDepth++];
        collection.Buffer.Clear();
        collection.Roots.Clear();
    }

    /// <summary>Copies one solution into the innermost collection.</summary>
    internal void AddCollected(Cell term)
    {
        Collection collection = _collections[_collectDepth - 1];
        collection.Roots.Add(collection.Buffer.Copy(this, term));
    }

    /// <summary>Ends the innermost collection and returns its solutions as a list.</summary>
    internal Cell EndCollect()
    {
        Collection collection = _collections[--_collectDepth];
        var origin = collection.Buffer.Materialize(this);

        Cell list = Cell.Atom(_symbols.EmptyList);
        for (var i = collection.Roots.Count - 1; i >= 0; i--)
        {
            Cell element = _heap[origin + collection.Roots[i]];
            list = CreateStructure(_symbols.ListFunctor, [element, list]);
        }

        return list;
    }

    /// <summary>Reserves <paramref name="count"/> contiguous heap cells and returns the first address.</summary>
    internal int ReserveHeap(int count)
    {
        EnsureHeap(count);
        var origin = _h;
        _h += count;
        return origin;
    }

    /// <summary>Writes a cell at an address reserved by <see cref="ReserveHeap"/>.</summary>
    internal void WriteHeap(int address, Cell cell) => _heap[address] = cell;

    /// <summary>
    /// Enters a dynamic predicate. The generation is snapshotted here, so the clauses this call sees
    /// are fixed even if the goal it runs asserts or retracts.
    /// </summary>
    private bool EnterDynamic(int functorId)
    {
        DynamicPredicate predicate = _program.FindDynamic(functorId) ?? throw PrologErrors.UndefinedProcedure(this, functorId);

        var generation = _program.Generation;
        Cell callKey = predicate.Arity >= 1 ? ClauseIndexing.CallKey(this, _x[0]) : ClauseIndexing.AnyKey;
        DynamicClause? clause = DynamicPredicate.FirstVisibleMatching(predicate.First, generation, callKey);
        if (clause is null)
        {
            return false;
        }

        DynamicClause? following = DynamicPredicate.FirstVisibleMatching(clause.Next, generation, callKey);
        if (following is not null)
        {
            PushChoicePoint(BytecodeProgram.NextClauseAddress);
            ref ChoicePoint point = ref _choicePoints[_b - 1];
            point.NextClause = following;
            point.ClauseGeneration = generation;
            point.IndexKey = callKey;
        }

        _pc = clause.CodeAddress;
        return true;
    }

    /// <summary>
    /// Dispatches an indexed static predicate: jump to the first clause whose first-argument key
    /// can match the call, pushing a choice point only if a later clause could match too.
    /// </summary>
    private bool EnterStatic(int tableId)
    {
        BytecodeProgram.StaticClauseIndex table = _program.StaticIndex(tableId);
        Cell callKey = ClauseIndexing.CallKey(this, _x[0]);

        var first = ClauseIndexing.NextMatch(table.Keys, 0, callKey);
        if (first < 0)
        {
            return false;
        }

        var following = ClauseIndexing.NextMatch(table.Keys, first + 1, callKey);
        if (following >= 0)
        {
            PushChoicePoint(BytecodeProgram.NextStaticClauseAddress);
            ref ChoicePoint point = ref _choicePoints[_b - 1];
            point.IndexTable = tableId;
            point.IndexNext = following;
            point.IndexKey = callKey;
        }

        _pc = table.Addresses[first];
        return true;
    }

    private bool HasCatchFrame()
    {
        for (var i = _b - 1; i >= 0; i--)
        {
            if (_choicePoints[i].CatchRecovery >= 0 && _choicePoints[i].CatchActive)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Unwinds to the innermost <c>catch/3</c> frame whose catcher unifies with the ball, and points
    /// execution at its recovery goal. Reports whether such a frame was found.
    /// </summary>
    private bool UnwindToCatch(PrologException error)
    {
        while (_b > 0)
        {
            var index = _b - 1;
            if (_choicePoints[index].CatchRecovery < 0 || !_choicePoints[index].CatchActive)
            {
                _b--;
                continue;
            }

            ChoicePoint frame = _choicePoints[index];

            // Pop the catch frame itself: a ball is caught at most once by the same catch/3.
            _b = index;
            UndoTrail(frame.TrailTop);
            _h = frame.HeapTop;
            _savedTop = frame.ArgumentBase + frame.ArgumentCount;
            _argumentCount = frame.ArgumentCount;
            _stackTop = frame.StackTop;
            _e = frame.Environment;
            _continuation = frame.Continuation;
            _b0 = frame.CutBarrier;
            _collectDepth = frame.CollectDepth;

            // Rebuild the ball above the restored heap top, then try the catcher against it.
            var origin = error.Ball!.Materialize(this);
            Cell ball = _heap[origin + error.BallRoot];
            Cell catcher = _stack[frame.Environment + FrameHeaderSize + frame.CatcherSlot];

            var mark = _tr;
            var previous = _forceTrail;
            _forceTrail = true;
            var matched = Unify(catcher, ball);
            _forceTrail = previous;

            if (matched)
            {
                _pc = frame.CatchRecovery;
                return true;
            }

            // This catcher does not apply; drop the ball copy and keep unwinding.
            UndoTrail(mark);
            _h = frame.HeapTop;
        }

        return false;
    }

    private sealed class Collection
    {
        internal TermBuffer Buffer { get; } = new();

        internal List<int> Roots { get; } = [];
    }

    /// <summary>Builds a compound term on the heap from <paramref name="arguments"/> and returns its cell.</summary>
    public Cell CreateStructure(int functorId, ReadOnlySpan<Cell> arguments)
    {
        EnsureHeap(1 + arguments.Length);
        var address = _h;
        _heap[_h++] = Cell.Functor(functorId);
        foreach (Cell argument in arguments)
        {
            _heap[_h++] = argument;
        }

        return Cell.Structure(address);
    }

    /// <summary>Builds a list from <paramref name="items"/> ending in <paramref name="tail"/>.</summary>
    public Cell CreateList(ReadOnlySpan<Cell> items, Cell tail)
    {
        Cell result = tail;
        for (var i = items.Length - 1; i >= 0; i--)
        {
            result = CreateStructure(_symbols.ListFunctor, [items[i], result]);
        }

        return result;
    }

    /// <summary>
    /// Reports whether two terms unify, leaving no bindings behind. Bindings made during the attempt
    /// are trailed unconditionally and undone before returning, which is what <c>\=/2</c> needs.
    /// </summary>
    public bool CanUnify(Cell left, Cell right)
    {
        var trailMark = _tr;
        var previous = _forceTrail;
        _forceTrail = true;
        var unified = Unify(left, right);
        _forceTrail = previous;
        UndoTrail(trailMark);
        return unified;
    }

    /// <summary>
    /// Unifies two terms without creating a new cyclic binding. A failed attempt restores every
    /// binding it made; a successful attempt retains the same bindings and trail state that
    /// ordinary unification would have retained.
    /// </summary>
    internal bool UnifyWithOccursCheck(Cell left, Cell right)
    {
        var trailMark = _tr;
        var previous = _forceTrail;
        var unified = false;
        _forceTrail = true;

        try
        {
            unified = UnifyCore(left, right, occursCheck: true);
            return unified;
        }
        finally
        {
            _forceTrail = previous;

            if (!unified)
            {
                UndoTrail(trailMark);
            }
            else if (!previous)
            {
                DiscardUnneededForcedTrail(trailMark);
            }
        }
    }

    /// <summary>
    /// Resolves the goal in argument register zero and transfers control to it. Chains of
    /// <c>call/1</c> wrappers are unwrapped first, so <c>call(call(G))</c> costs nothing extra.
    /// </summary>
    private bool MetaCall()
    {
        Cell goal = Dereference(_x[0]);

        while (goal.Tag == CellTag.Structure && _heap[goal.Index].Index == _callFunctor)
        {
            goal = Dereference(_heap[goal.Index + 1]);
        }

        // A control construct has to be whole before any of it runs: ISO 7.8.3 makes
        // call((fail, 4)) a type error rather than a failure, even though the conjunction would
        // never reach the 4.
        _callableVisited.Clear();
        RequireCallable(goal, goal);

        if (IsControlGoal(goal) && _program.RuntimeCompiler is not null)
        {
            var entry = _program.RuntimeCompiler.CompileControlGoal(this, goal, _x, out var controlArity);
            _argumentCount = controlArity;
            _continuation = _pc;
            _b0 = _b;
            _pc = entry;
            return true;
        }

        int functorId;
        int arity;

        switch (goal.Tag)
        {
            case CellTag.Atom:
                functorId = _symbols.InternFunctor(goal.Index, 0);
                arity = 0;
                break;

            case CellTag.Structure:
                functorId = _heap[goal.Index].Index;
                arity = _symbols.ArityOf(functorId);
                for (var i = 0; i < arity; i++)
                {
                    _x[i] = _heap[goal.Index + 1 + i];
                }

                break;

            case CellTag.Reference:
                throw PrologErrors.Instantiation(this);

            default:
                throw PrologErrors.Type(this, "callable", goal);
        }

        _argumentCount = arity;

        if (_program.IsStrictIsoExtension(functorId))
        {
            throw PrologErrors.Permission(this, "access", "implementation_specific_feature", functorId);
        }

        if (_program.Builtins.TryGetId(functorId, out var builtinId))
        {
            _currentBuiltin = builtinId;
            return _program.Builtins.Implementation(builtinId)(this);
        }

        _continuation = _pc;
        _b0 = _b;
        return TryEntryPoint(functorId, out _pc);
    }

    /// <summary>
    /// Whether <paramref name="goal"/> needs the clause compiler's inline control lowering to give
    /// cuts reached through <c>call/1</c> their ISO meta-call scope.
    /// </summary>
    private bool IsControlGoal(Cell goal)
    {
        if (goal.Tag != CellTag.Structure)
        {
            return false;
        }

        Functor functor = _symbols.GetFunctor(_heap[goal.Index].Index);
        var name = _symbols.AtomName(functor.NameAtom);
        return (functor.Arity == 2 && name is "," or ";" or "->" or "*->") || (functor.Arity == 1 && name == "\\+");
    }

    /// <summary>Requests that the current run stop with <paramref name="exitCode"/>, as <c>halt/1</c> does.</summary>
    public void RequestHalt(int exitCode)
    {
        ExitCode = exitCode;
        _halted = true;

        // A program that writes a file and then halts must find the file written, so what it opened
        // is flushed and closed here rather than left to a finalizer that may never run.
        Streams.CloseAll();
    }

    /// <summary>Follows a reference chain to the term it denotes, or to the unbound variable that ends it.</summary>
    public Cell Dereference(Cell cell)
    {
        while (cell.Tag == CellTag.Reference)
        {
            Cell target = _heap[cell.Index];
            if (target.Tag == CellTag.Reference && target.Index == cell.Index)
            {
                return cell;
            }

            cell = target;
        }

        return cell;
    }

    private Cell _occursCycleVariable;
    private Cell _occursCycleTerm;
    private bool _occursCycleDetected;

    /// <summary>
    /// Unifies two terms, trailing every binding so that backtracking can undo them. The
    /// <c>occurs_check</c> flag guards this entry: <c>false</c> is the unchanged fast path, and
    /// the other modes route through the occurs-check unifier (ADR 0046). Write-mode head
    /// unification does not pass through here and is not guarded.
    /// </summary>
    public bool Unify(Cell left, Cell right)
    {
        OccursCheckMode mode = _program.Flags.OccursCheck;
        return mode == OccursCheckMode.False ? UnifyCore(left, right, occursCheck: false) : UnifyGuarded(left, right, mode);
    }

    private bool UnifyGuarded(Cell left, Cell right, OccursCheckMode mode)
    {
        _occursCycleDetected = false;
        var unified = UnifyWithOccursCheck(left, right);

        if (!unified && mode == OccursCheckMode.Error && _occursCycleDetected)
        {
            throw PrologErrors.OccursCheck(this, _occursCycleVariable, _occursCycleTerm);
        }

        return unified;
    }

    private bool UnifyCore(Cell left, Cell right, bool occursCheck)
    {
        _unificationTop = 0;
        _unificationVisited.Clear();
        PushUnification(left, right);

        while (_unificationTop > 0)
        {
            Cell b = _unificationStack[--_unificationTop];
            Cell a = _unificationStack[--_unificationTop];
            a = Dereference(a);
            b = Dereference(b);

            if (a == b)
            {
                continue;
            }

            if (a.Tag == CellTag.Reference)
            {
                // Bind the younger variable to the older one so the binding survives heap truncation.
                if (b.Tag == CellTag.Reference)
                {
                    if (b.Index > a.Index)
                    {
                        Bind(b.Index, a);
                    }
                    else
                    {
                        Bind(a.Index, b);
                    }

                    continue;
                }

                if (occursCheck && OccursIn(a.Index, b))
                {
                    _occursCycleVariable = a;
                    _occursCycleTerm = b;
                    _occursCycleDetected = true;
                    _unificationTop = 0;
                    return false;
                }

                Bind(a.Index, b);
                continue;
            }

            if (b.Tag == CellTag.Reference)
            {
                if (occursCheck && OccursIn(b.Index, a))
                {
                    _occursCycleVariable = b;
                    _occursCycleTerm = a;
                    _occursCycleDetected = true;
                    _unificationTop = 0;
                    return false;
                }

                Bind(b.Index, a);
                continue;
            }

            if (a.Tag != CellTag.Structure || b.Tag != CellTag.Structure)
            {
                _unificationTop = 0;
                return false;
            }

            var functorId = _heap[a.Index].Index;
            if (functorId != _heap[b.Index].Index)
            {
                _unificationTop = 0;
                return false;
            }

            // Rational trees can lead back to the same pair of structures. Their arguments were
            // already scheduled on the first visit, so revisiting the pair adds no constraint and
            // would otherwise make ordinary unification loop forever.
            var lower = (uint)Math.Min(a.Index, b.Index);
            var upper = (uint)Math.Max(a.Index, b.Index);
            var pair = ((ulong)lower << 32) | upper;
            if (!_unificationVisited.Add(pair))
            {
                continue;
            }

            var arity = _symbols.ArityOf(functorId);
            for (var i = arity; i >= 1; i--)
            {
                PushUnification(_heap[a.Index + i], _heap[b.Index + i]);
            }
        }

        return true;
    }

    private void ResetState()
    {
        // Backtrackable global assignments must not outlive their top-level goal: fire the whole
        // undo stack before the trail and heap they are ordered against are discarded.
        UndoTrail(0);

        _h = 0;
        _tr = 0;
        _stackTop = 0;
        _e = -1;
        _b = 0;
        _b0 = 0;
        _savedTop = 0;
        _unificationTop = 0;
        _occursCheckTop = 0;
        _writeMode = false;
        _structureArgument = 0;
        _argumentCount = 0;
        _halted = false;
        _forceTrail = false;
        _solutionPending = false;
        _currentBuiltin = -1;
        _collectDepth = 0;
        ExitCode = 0;
    }

    private bool TryEntryPoint(int functorId, out int entry)
    {
        entry = _program.EntryPointOf(functorId);
        if (_program.IsDefined(functorId))
        {
            return true;
        }

        switch (_program.Flags.Unknown)
        {
            case UnknownProcedureAction.Fail:
                return false;

            case UnknownProcedureAction.Warning:
                Streams.UserError.Writer!.WriteLine($"Warning: undefined procedure {_symbols.DescribeFunctor(functorId)}");
                return false;

            default:
                throw PrologErrors.UndefinedProcedure(this, functorId);
        }
    }

    private void Allocate(int slots)
    {
        var previousTop = _stackTop;

        // A frame may not overwrite stack space a live choice point still needs.
        var frameBase = _b > 0 ? Math.Max(_stackTop, _choicePoints[_b - 1].StackTop) : _stackTop;
        EnsureStack(frameBase + FrameHeaderSize + slots);

        _stack[frameBase + FrameContinuation] = Cell.Integer60(_continuation);
        _stack[frameBase + FrameEnvironment] = Cell.Integer60(_e);
        _stack[frameBase + FramePreviousTop] = Cell.Integer60(previousTop);
        _stack[frameBase + FrameCutBarrier] = Cell.Integer60(_b0);

        _e = frameBase;
        _stackTop = frameBase + FrameHeaderSize + slots;
    }

    private void Deallocate()
    {
        var frame = _e;
        _continuation = (int)_stack[frame + FrameContinuation].Integer;
        _stackTop = (int)_stack[frame + FramePreviousTop].Integer;
        _e = (int)_stack[frame + FrameEnvironment].Integer;
    }

    private Cell BeginStructure(int functorId)
    {
        var arity = _symbols.ArityOf(functorId);
        EnsureHeap(1 + arity);
        var address = _h;
        _heap[_h++] = Cell.Functor(functorId);
        _writeMode = true;
        return Cell.Structure(address);
    }

    private bool GetStructure(int functorId, Cell subject)
    {
        Cell cell = Dereference(subject);

        if (cell.Tag == CellTag.Reference)
        {
            Cell structure = BeginStructure(functorId);
            Bind(cell.Index, structure);
            return true;
        }

        if (cell.Tag == CellTag.Structure && _heap[cell.Index].Index == functorId)
        {
            _structureArgument = cell.Index + 1;
            _writeMode = false;
            return true;
        }

        return false;
    }

    private bool UnifyConstantWith(Cell subject, Cell constant)
    {
        Cell cell = Dereference(subject);
        if (cell.Tag == CellTag.Reference)
        {
            Bind(cell.Index, constant);
            return true;
        }

        return cell == constant;
    }

    /// <summary>
    /// Pushes a fresh unbound variable and returns its cell. The cell is returned rather than the
    /// address because growing the heap replaces the array, and an expression such as
    /// <c>_heap[NewVariable()]</c> would index the array that existed before the growth.
    /// </summary>
    private Cell NewVariable()
    {
        EnsureHeap(1);
        var address = _h;
        var cell = Cell.Reference(address);
        _heap[address] = cell;
        _h = address + 1;
        return cell;
    }

    private void Bind(int address, Cell value)
    {
        _heap[address] = value;

        // Only bindings older than the newest choice point need undoing; younger cells vanish with the
        // heap. A tentative unification suspends that reasoning and trails everything.
        if (_forceTrail || (_b > 0 && address < _choicePoints[_b - 1].HeapTop))
        {
            if (_tr == _trail.Length)
            {
                Array.Resize(ref _trail, _trail.Length * 2);
            }

            _trail[_tr++] = address;
        }
    }

    private void UndoTrail(int mark)
    {
        if (_valueUndoTop == 0)
        {
            while (_tr > mark)
            {
                var address = _trail[--_tr];
                _heap[address] = Cell.Reference(address);
            }
        }
        else
        {
            // A binding and a setarg/3 can target the same heap address, so their restores must
            // interleave in exact reverse chronology. A value-undo entry's mark is the trail
            // position just above its own sentinel: it fires when the unwind reaches that
            // position, before the sentinel itself pops.
            while (_tr > mark)
            {
                while (_valueUndoTop > 0 && _valueUndo[_valueUndoTop - 1].TrailMark == _tr)
                {
                    ref ValueUndo assignment = ref _valueUndo[--_valueUndoTop];
                    _heap[assignment.Address] = assignment.Previous;
                }

                var address = _trail[--_tr];
                _heap[address] = Cell.Reference(address);
            }
        }

        // A backtrackable global assignment is undone when the trail unwinds below its sentinel
        // entry. Dictionary state is disjoint from the heap, so its order against the loops above
        // does not matter. Tentative-unification undos always target a mark at or above every
        // recorded assignment, so they can never fire one.
        while (_globalUndoTop > 0 && _globalUndo[_globalUndoTop - 1].TrailMark > mark)
        {
            ref GlobalUndo undo = ref _globalUndo[--_globalUndoTop];
            if (undo.HadValue)
            {
                _globals[undo.KeyAtom] = undo.Previous;
            }
            else
            {
                _globals.Remove(undo.KeyAtom);
            }
        }
    }

    /// <summary>
    /// Destructively assigns the heap cell at <paramref name="address"/>, a structure argument
    /// slot. A backtrackable assignment records the previous cell on the value-undo stack behind
    /// its own sentinel trail entry — unless the slot is younger than every choice point, in
    /// which case backtracking discards it with the heap and nothing needs recording.
    /// </summary>
    internal void SetArgument(int address, Cell value, bool backtrackable)
    {
        if (backtrackable && _b > 0 && address < _choicePoints[_b - 1].HeapTop)
        {
            Cell sentinel = NewVariable();
            if (_tr == _trail.Length)
            {
                Array.Resize(ref _trail, _trail.Length * 2);
            }

            _trail[_tr++] = sentinel.Index;

            if (_valueUndoTop == _valueUndo.Length)
            {
                Array.Resize(ref _valueUndo, _valueUndo.Length * 2);
            }

            ref ValueUndo undo = ref _valueUndo[_valueUndoTop++];
            undo.TrailMark = _tr;
            undo.Address = address;
            undo.Previous = _heap[address];
        }

        _heap[address] = value;
    }

    private struct ValueUndo
    {
        public int TrailMark;
        public int Address;
        public Cell Previous;
    }

    /// <summary>
    /// Sets the global variable named by <paramref name="keyAtom"/>. A backtrackable assignment
    /// stores the live cell and is undone when the trail unwinds below the sentinel entry pushed
    /// here; a non-backtrackable one stores a detached copy that survives any unwinding.
    /// </summary>
    internal void SetGlobal(int keyAtom, Cell value, bool backtrackable)
    {
        if (!backtrackable)
        {
            var buffer = new TermBuffer();
            var root = buffer.Copy(this, value);
            _globals[keyAtom] = new GlobalVariable(default, buffer, root);
            return;
        }

        // The sentinel gives this assignment its own trail position, so undo entries stay totally
        // ordered against choice-point trail tops even when nothing else was trailed in between.
        Cell sentinel = NewVariable();
        if (_tr == _trail.Length)
        {
            Array.Resize(ref _trail, _trail.Length * 2);
        }

        _trail[_tr++] = sentinel.Index;

        if (_globalUndoTop == _globalUndo.Length)
        {
            Array.Resize(ref _globalUndo, _globalUndo.Length * 2);
        }

        ref GlobalUndo undo = ref _globalUndo[_globalUndoTop++];
        undo.TrailMark = _tr;
        undo.KeyAtom = keyAtom;
        undo.HadValue = _globals.TryGetValue(keyAtom, out GlobalVariable previous);
        undo.Previous = previous;

        _globals[keyAtom] = new GlobalVariable(value, null, 0);
    }

    /// <summary>
    /// Reads the global variable named by <paramref name="keyAtom"/>. A detached value
    /// materializes as a fresh copy on the heap, the way a collected solution does.
    /// </summary>
    internal bool TryGetGlobal(int keyAtom, out Cell value)
    {
        if (!_globals.TryGetValue(keyAtom, out GlobalVariable entry))
        {
            value = default;
            return false;
        }

        if (entry.Detached is TermBuffer buffer)
        {
            var origin = buffer.Materialize(this);
            value = _heap[origin + entry.Root];
            return true;
        }

        value = entry.Live;
        return true;
    }

    private readonly record struct GlobalVariable(Cell Live, TermBuffer? Detached, int Root);

    private struct GlobalUndo
    {
        public int TrailMark;
        public int KeyAtom;
        public bool HadValue;
        public GlobalVariable Previous;
    }

    private void PushChoicePoint(int alternative)
    {
        if (_b == _choicePoints.Length)
        {
            Array.Resize(ref _choicePoints, _choicePoints.Length * 2);
        }

        if (_savedTop + _argumentCount > _savedArguments.Length)
        {
            Array.Resize(ref _savedArguments, Math.Max(_savedArguments.Length * 2, _savedTop + _argumentCount));
        }

        Array.Copy(_x, 0, _savedArguments, _savedTop, _argumentCount);

        ref ChoicePoint point = ref _choicePoints[_b];
        point.Alternative = alternative;
        point.ArgumentBase = _savedTop;
        point.ArgumentCount = _argumentCount;
        point.HeapTop = _h;
        point.TrailTop = _tr;

        // The protection watermark must be monotone up the choice-point stack. Deallocate lowers
        // _stackTop below frames that older choice points still reference; recording the raw value
        // here would let Allocate reuse their stack space and clobber a live frame.
        point.StackTop = _b > 0 ? Math.Max(_stackTop, _choicePoints[_b - 1].StackTop) : _stackTop;
        point.Environment = _e;
        point.Continuation = _continuation;
        point.CutBarrier = _b0;
        point.CollectDepth = _collectDepth;
        point.CatchRecovery = -1;
        point.CatcherSlot = 0;
        point.CatchActive = false;
        point.NextClause = null;
        point.ClauseGeneration = 0;

        _savedTop += _argumentCount;
        _b++;
    }

    private bool Backtrack()
    {
        if (_b == 0)
        {
            return false;
        }

        ref ChoicePoint point = ref _choicePoints[_b - 1];
        UndoTrail(point.TrailTop);
        _h = point.HeapTop;
        Array.Copy(_savedArguments, point.ArgumentBase, _x, 0, point.ArgumentCount);
        _argumentCount = point.ArgumentCount;
        _savedTop = point.ArgumentBase + point.ArgumentCount;
        _stackTop = point.StackTop;
        _e = point.Environment;
        _continuation = point.Continuation;
        _b0 = point.CutBarrier;
        _collectDepth = point.CollectDepth;
        _pc = point.Alternative;
        return true;
    }

    private void CutTo(int barrier)
    {
        if (barrier >= _b)
        {
            return;
        }

        _b = barrier;
        _savedTop = barrier > 0 ? _choicePoints[barrier - 1].ArgumentBase + _choicePoints[barrier - 1].ArgumentCount : 0;
    }

    private void PushUnification(Cell left, Cell right)
    {
        if (_unificationTop + 2 > _unificationStack.Length)
        {
            Array.Resize(ref _unificationStack, _unificationStack.Length * 2);
        }

        _unificationStack[_unificationTop++] = left;
        _unificationStack[_unificationTop++] = right;
    }

    private bool OccursIn(int variableAddress, Cell term)
    {
        var epoch = NextOccursCheckEpoch();
        _occursCheckTop = 0;
        PushOccursCheck(term);

        while (_occursCheckTop > 0)
        {
            Cell cell = Dereference(_occursCheckStack[--_occursCheckTop]);

            if (cell.Tag == CellTag.Reference)
            {
                if (cell.Index == variableAddress)
                {
                    _occursCheckTop = 0;
                    return true;
                }

                continue;
            }

            if (cell.Tag != CellTag.Structure || _occursCheckVisited[cell.Index] == epoch)
            {
                continue;
            }

            _occursCheckVisited[cell.Index] = epoch;
            var arity = _symbols.ArityOf(_heap[cell.Index].Index);
            for (var i = arity; i >= 1; i--)
            {
                PushOccursCheck(_heap[cell.Index + i]);
            }
        }

        return false;
    }

    private int NextOccursCheckEpoch()
    {
        _occursCheckEpoch = unchecked(_occursCheckEpoch + 1);
        if (_occursCheckEpoch > 0)
        {
            return _occursCheckEpoch;
        }

        Array.Clear(_occursCheckVisited);
        _occursCheckEpoch = 1;
        return _occursCheckEpoch;
    }

    private void PushOccursCheck(Cell cell)
    {
        if (_occursCheckTop == _occursCheckStack.Length)
        {
            Array.Resize(ref _occursCheckStack, _occursCheckStack.Length * 2);
        }

        _occursCheckStack[_occursCheckTop++] = cell;
    }

    private void DiscardUnneededForcedTrail(int trailMark)
    {
        if (_b == 0)
        {
            _tr = trailMark;
            return;
        }

        var heapTop = _choicePoints[_b - 1].HeapTop;
        var write = trailMark;
        for (var read = trailMark; read < _tr; read++)
        {
            if (_trail[read] < heapTop)
            {
                _trail[write++] = _trail[read];
            }
        }

        _tr = write;
    }

    private void EnsureHeap(int required)
    {
        if (_h + required <= _heap.Length)
        {
            return;
        }

        Array.Resize(ref _heap, Math.Max(_heap.Length * 2, _h + required));
        Array.Resize(ref _occursCheckVisited, _heap.Length);
    }

    private void EnsureStack(int required)
    {
        if (required <= _stack.Length)
        {
            return;
        }

        Array.Resize(ref _stack, Math.Max(_stack.Length * 2, required));
    }
}
