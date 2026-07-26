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

    private const int Undefined = -1;

    private int[] _code = new int[1024];
    private Cell[] _constants = new Cell[64];
    private int[] _entryPoints = new int[64];
    private int _constantCount;

    /// <summary>Creates an empty program with its own symbol table and builtin registry.</summary>
    public BytecodeProgram()
    {
        Symbols = new SymbolTable();
        Builtins = new BuiltinRegistry(Symbols);
        Array.Fill(_entryPoints, Undefined);
        _code[0] = (int)OpCode.Stop;
        CodeLength = 1;
    }

    /// <summary>The atoms, functors, and floats this program refers to.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>The native predicates this program may call.</summary>
    public BuiltinRegistry Builtins { get; }

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
