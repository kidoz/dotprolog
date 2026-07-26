namespace Prolog.Runtime;

/// <summary>
/// The Prolog execution engine: heap, trail, environment stack, choice-point stack, argument
/// registers, and the instruction dispatch loop.
/// </summary>
/// <remarks>
/// The engine owns its control state. Prolog calls are jumps inside a single dispatch loop rather
/// than CLR method calls, so recursion depth is bounded by the machine's own stacks; failure is a
/// return value rather than an exception. See <c>.agents/contexts/conventions/STYLE_PROFILE.md</c>.
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

    private int _pc;
    private int _continuation;
    private int _structureArgument;
    private bool _writeMode;
    private int _argumentCount;
    private bool _halted;

    /// <summary>Creates a machine that executes <paramref name="program"/>.</summary>
    public Machine(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        _program = program;
        _symbols = program.Symbols;
        Output = Console.Out;
    }

    /// <summary>Where <c>write/1</c> and friends send their output.</summary>
    public TextWriter Output { get; set; }

    /// <summary>The exit code requested by <c>halt/1</c>, or zero.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The program being executed.</summary>
    public BytecodeProgram Program => _program;

    /// <summary>The program's symbol table.</summary>
    public SymbolTable Symbols => _symbols;

    /// <summary>Proves the goal <c>name/arity</c>, which must be a defined predicate taking no arguments.</summary>
    /// <exception cref="PrologException">The predicate is not defined.</exception>
    public RunResult Solve(int functorId)
    {
        int entry = _program.EntryPointOf(functorId);
        if (entry < 0)
        {
            throw new PrologException($"existence_error(procedure, {_symbols.DescribeFunctor(functorId)})");
        }

        return Run(entry);
    }

    /// <summary>Runs the instruction stream from <paramref name="entryAddress"/> until it stops.</summary>
    /// <exception cref="PrologException">A called predicate is not defined.</exception>
    public RunResult Run(int entryAddress)
    {
        ResetState();

        // Cached for the dispatch loop; the program is not mutated while a goal is running.
        int[] code = _program.Code;
        Cell[] constants = _program.Constants;

        _pc = entryAddress;
        _continuation = BytecodeProgram.TopLevelReturnAddress;

        while (true)
        {
            bool proved = true;
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
                    int functorId = code[_pc++];
                    _argumentCount = code[_pc++];
                    _continuation = _pc;
                    _b0 = _b;
                    _pc = EntryPointOf(functorId);
                    break;
                }

                case OpCode.Execute:
                {
                    int functorId = code[_pc++];
                    _argumentCount = code[_pc++];
                    _b0 = _b;
                    _pc = EntryPointOf(functorId);
                    break;
                }

                case OpCode.CallBuiltin:
                {
                    int builtinId = code[_pc++];
                    _argumentCount = code[_pc++];
                    proved = _program.Builtins.Implementation(builtinId)(this);
                    break;
                }

                case OpCode.Proceed:
                    _pc = _continuation;
                    break;

                case OpCode.Cut:
                    CutTo((int)_stack[_e + FrameCutBarrier].Integer);
                    break;

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
                    int slot = code[_pc++];
                    _stack[_e + FrameHeaderSize + slot] = _x[code[_pc++]];
                    break;
                }

                case OpCode.GetValue:
                {
                    int slot = code[_pc++];
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
                    int functorId = code[_pc++];
                    proved = GetStructure(functorId, _x[code[_pc++]]);
                    break;
                }

                case OpCode.GetStructureSlot:
                {
                    int functorId = code[_pc++];
                    proved = GetStructure(functorId, _stack[_e + FrameHeaderSize + code[_pc++]]);
                    break;
                }

                case OpCode.UnifyVariable:
                {
                    int slot = code[_pc++];
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
                    int slot = code[_pc++];
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
                    int slot = code[_pc++];
                    Cell variable = NewVariable();
                    _stack[_e + FrameHeaderSize + slot] = variable;
                    _x[code[_pc++]] = variable;
                    break;
                }

                case OpCode.PutValue:
                {
                    int slot = code[_pc++];
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
                    int functorId = code[_pc++];
                    _x[code[_pc++]] = BeginStructure(functorId);
                    break;
                }

                case OpCode.PutStructureSlot:
                {
                    int functorId = code[_pc++];
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

    /// <summary>Returns the heap cell at <paramref name="address"/>.</summary>
    public Cell HeapAt(int address) => _heap[address];

    /// <summary>Requests that the current run stop with <paramref name="exitCode"/>, as <c>halt/1</c> does.</summary>
    public void RequestHalt(int exitCode)
    {
        ExitCode = exitCode;
        _halted = true;
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

    /// <summary>Unifies two terms, trailing every binding so that backtracking can undo them.</summary>
    public bool Unify(Cell left, Cell right)
    {
        _unificationTop = 0;
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
                if (b.Tag == CellTag.Reference && b.Index > a.Index)
                {
                    Bind(b.Index, a);
                }
                else
                {
                    Bind(a.Index, b);
                }

                continue;
            }

            if (b.Tag == CellTag.Reference)
            {
                Bind(b.Index, a);
                continue;
            }

            if (a.Tag != CellTag.Structure || b.Tag != CellTag.Structure)
            {
                _unificationTop = 0;
                return false;
            }

            int functorId = _heap[a.Index].Index;
            if (functorId != _heap[b.Index].Index)
            {
                _unificationTop = 0;
                return false;
            }

            int arity = _symbols.ArityOf(functorId);
            for (int i = arity; i >= 1; i--)
            {
                PushUnification(_heap[a.Index + i], _heap[b.Index + i]);
            }
        }

        return true;
    }

    private void ResetState()
    {
        _h = 0;
        _tr = 0;
        _stackTop = 0;
        _e = -1;
        _b = 0;
        _b0 = 0;
        _savedTop = 0;
        _unificationTop = 0;
        _writeMode = false;
        _structureArgument = 0;
        _argumentCount = 0;
        _halted = false;
        ExitCode = 0;
    }

    private int EntryPointOf(int functorId)
    {
        int entry = _program.EntryPointOf(functorId);
        if (entry < 0)
        {
            throw new PrologException($"existence_error(procedure, {_symbols.DescribeFunctor(functorId)})");
        }

        return entry;
    }

    private void Allocate(int slots)
    {
        int previousTop = _stackTop;

        // A frame may not overwrite stack space a live choice point still needs.
        int frameBase = _b > 0 ? Math.Max(_stackTop, _choicePoints[_b - 1].StackTop) : _stackTop;
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
        int frame = _e;
        _continuation = (int)_stack[frame + FrameContinuation].Integer;
        _stackTop = (int)_stack[frame + FramePreviousTop].Integer;
        _e = (int)_stack[frame + FrameEnvironment].Integer;
    }

    private Cell BeginStructure(int functorId)
    {
        int arity = _symbols.ArityOf(functorId);
        EnsureHeap(1 + arity);
        int address = _h;
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
        int address = _h;
        var cell = Cell.Reference(address);
        _heap[address] = cell;
        _h = address + 1;
        return cell;
    }

    private void Bind(int address, Cell value)
    {
        _heap[address] = value;

        // Only bindings older than the newest choice point need undoing; younger cells vanish with the heap.
        if (_b > 0 && address < _choicePoints[_b - 1].HeapTop)
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
        while (_tr > mark)
        {
            int address = _trail[--_tr];
            _heap[address] = Cell.Reference(address);
        }
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
        point.StackTop = _stackTop;
        point.Environment = _e;
        point.Continuation = _continuation;
        point.CutBarrier = _b0;

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

    private void EnsureHeap(int required)
    {
        if (_h + required <= _heap.Length)
        {
            return;
        }

        Array.Resize(ref _heap, Math.Max(_heap.Length * 2, _h + required));
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
