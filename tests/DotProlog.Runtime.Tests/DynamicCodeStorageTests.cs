using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class DynamicCodeStorageTests
{
    [Fact]
    public void ReuseRelocatesBranchAndConstantOperandsWhilePinnedStorageStaysValid()
    {
        var program = new BytecodeProgram();
        DynamicCodeBlock donor = EmitPrivateBlock(program, 10);
        var pinned = program.Emit(OpCode.Stop);
        var pinnedConstant = program.AddConstant(Cell.Integer60(999));
        program.ReleaseDynamicCode(donor);
        var append = program.CodeLength;
        var constants = program.ConstantCount;

        DynamicCodeBlock reused = EmitPrivateBlock(program, 20);
        Assert.Equal(donor.CodeStart, reused.CodeStart);
        Assert.Equal(donor.ConstantStart, reused.ConstantStart);
        Assert.Equal(append, program.CodeLength);
        Assert.Equal(constants, program.ConstantCount);
        Assert.Equal((int)OpCode.Stop, program.Code[pinned]);
        Assert.Equal(Cell.Integer60(999), program.Constants[pinnedConstant]);

        var address = reused.CodeStart;
        Assert.Equal((int)OpCode.TryBranch, program.Code[address]);
        Assert.Equal(7, program.Code[address + 1]); // slot, not an address
        Assert.Equal(reused.CodeStart, program.Code[address + 2]);
        Assert.Equal(reused.CodeStart, program.Code[address + 5]); // PushCatch recovery
        Assert.Equal(reused.CodeStart, program.Code[address + 8]); // PopCatch reactivation
        Assert.Equal(reused.CodeStart, program.Code[address + 10]); // Jump
        Assert.Equal(reused.CodeStart, program.Code[address + 12]); // TryMeElse
        Assert.Equal(reused.CodeStart, program.Code[address + 14]); // RetryMeElse
        Assert.Equal(reused.ConstantStart, program.Code[address + 16]); // GetConstant
        Assert.Equal(9, program.Code[address + 17]); // argument register
        Assert.Equal(reused.ConstantStart + 1, program.Code[address + 19]); // PutConstant
        Assert.Equal(reused.ConstantStart, program.Code[address + 22]); // UnifyConstant
        Assert.Equal(Cell.Integer60(20), program.Constants[reused.ConstantStart]);
        Assert.Equal(Cell.Integer60(21), program.Constants[reused.ConstantStart + 1]);

        program.ReleaseDynamicCode(reused);
        Assert.Equal((int)OpCode.Fail, program.Code[reused.CodeStart]);
        Assert.Equal(Cell.Integer60(999), program.Constants[pinnedConstant]);
    }

    [Fact]
    public void FreeRangesSplitCoalesceAndTrimWithoutMovingThePinnedTail()
    {
        var ranges = new ReusableRanges();
        Assert.Equal(40, ranges.Release(10, 20, 40));
        Assert.Equal(10, ranges.Allocate(7));
        Assert.Equal(17, ranges.Allocate(13));
        Assert.Equal(-1, ranges.Allocate(1));
        Assert.Equal(40, ranges.Release(10, 7, 40));
        Assert.Equal(40, ranges.Release(17, 13, 40));
        Assert.Equal(10, ranges.Allocate(20));
        Assert.Equal(40, ranges.Release(10, 20, 40));
        Assert.Equal(10, ranges.Release(30, 10, 40));
        Assert.Equal(-1, ranges.Allocate(1));
    }

    [Fact]
    public void OverlappingReleaseIsRejectedWithoutLosingAdjacentFreeSpace()
    {
        var ranges = new ReusableRanges();
        ranges.Release(10, 10, 100);
        ranges.Release(30, 10, 100);
        Assert.Throws<InvalidOperationException>(() => ranges.Release(20, 15, 100));
        Assert.Equal(10, ranges.Allocate(10));
        Assert.Equal(30, ranges.Allocate(10));
        Assert.Equal(-1, ranges.Allocate(1));
    }

    [Fact]
    public void ReleasingTheLastPrivateClauseTrimsBothAddressSpaces()
    {
        var program = new BytecodeProgram();
        var code = program.CodeLength;
        var constants = program.ConstantCount;
        DynamicCodeBlock block = EmitPrivateBlock(program, 40);
        program.ReleaseDynamicCode(block);
        Assert.Equal(code, program.CodeLength);
        Assert.Equal(constants, program.ConstantCount);
    }

    [Fact]
    public void CodeAndConstantRangesCanBeReusedIndependentlyAndExecuted()
    {
        var program = new BytecodeProgram();
        DynamicCodeBlock first = EmitLiterals(program, 8, 1, 100);
        program.Emit(OpCode.Stop);
        var pinned = program.AddConstant(Cell.Integer60(777));
        DynamicCodeBlock second = EmitLiterals(program, 1, 4, 200);
        program.Emit(OpCode.Stop);
        program.AddConstant(Cell.Integer60(888));
        program.ReleaseDynamicCode(first);
        program.ReleaseDynamicCode(second);

        DynamicCodeBlock reused = EmitLiterals(program, 8, 4, 300);
        Assert.Equal(second.CodeStart, reused.CodeStart);
        Assert.Equal(first.ConstantStart, reused.ConstantStart);
        Assert.Equal(Cell.Integer60(777), program.Constants[pinned]);
        using var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Run(reused.CodeStart));
        Assert.Equal(Cell.Integer60(303), machine.Argument(0));
    }

    private static DynamicCodeBlock EmitLiterals(BytecodeProgram program, int constants, int instructions, int value)
    {
        var constantStart = program.ConstantCount;
        for (var i = 0; i < constants; i++)
            program.AddConstant(Cell.Integer60(value + i));
        var start = program.CodeLength;
        for (var i = 0; i < instructions; i++)
            program.Emit(OpCode.PutConstant, constantStart + i % constants, 0);
        program.Emit(OpCode.Proceed);
        return program.StoreDynamicCode(start, constantStart);
    }

    private static DynamicCodeBlock EmitPrivateBlock(BytecodeProgram program, int value)
    {
        var constantStart = program.ConstantCount;
        var first = program.AddConstant(Cell.Integer60(value));
        var second = program.AddConstant(Cell.Integer60(value + 1));
        var start = program.CodeLength;
        program.Emit(OpCode.TryBranch, 7, start);
        program.Emit(OpCode.PushCatch, 8, start);
        program.Emit(OpCode.PopCatch, 8, start);
        program.Emit(OpCode.Jump, start);
        program.Emit(OpCode.TryMeElse, start);
        program.Emit(OpCode.RetryMeElse, start);
        program.Emit(OpCode.GetConstant, first, 9);
        program.Emit(OpCode.PutConstant, second, 10);
        program.Emit(OpCode.UnifyConstant, first);
        program.Emit(OpCode.Proceed);
        return program.StoreDynamicCode(start, constantStart);
    }
}
