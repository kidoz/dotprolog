namespace DotProlog.Runtime;

/// <summary>Operand metadata for compiler extraction and private-clause relocation, outside dispatch.</summary>
internal static class BytecodeLayout
{
    internal static int OperandCount(OpCode code) =>
        code switch
        {
            OpCode.Stop
            or OpCode.Deallocate
            or OpCode.Proceed
            or OpCode.Cut
            or OpCode.MetaCall
            or OpCode.TrustMe
            or OpCode.NextClause
            or OpCode.RedoBuiltin
            or OpCode.Fail
            or OpCode.NextStaticClause
            or OpCode.WakeReturn => 0,
            OpCode.Allocate
            or OpCode.MarkBarrier
            or OpCode.Jump
            or OpCode.CutTo
            or OpCode.SoftCut
            or OpCode.ReactivateCatch
            or OpCode.TryMeElse
            or OpCode.RetryMeElse
            or OpCode.UnifyVariable
            or OpCode.UnifyValue
            or OpCode.UnifyConstant
            or OpCode.InitVariable
            or OpCode.EnterDynamic
            or OpCode.EnterStatic => 1,
            OpCode.Call
            or OpCode.Execute
            or OpCode.CallBuiltin
            or OpCode.TryBranch
            or OpCode.PushCatch
            or OpCode.PopCatch
            or OpCode.GetVariable
            or OpCode.GetValue
            or OpCode.GetConstant
            or OpCode.GetStructureArgument
            or OpCode.GetStructureSlot
            or OpCode.PutVariable
            or OpCode.PutValue
            or OpCode.PutConstant
            or OpCode.PutStructureArgument
            or OpCode.PutStructureSlot => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown bytecode instruction."),
        };

    internal static int AddressOperand(OpCode code) =>
        code switch
        {
            OpCode.Jump or OpCode.TryMeElse or OpCode.RetryMeElse => 1,
            OpCode.TryBranch or OpCode.PushCatch or OpCode.PopCatch => 2,
            _ => 0,
        };

    internal static bool HasConstantOperand(OpCode code) =>
        code is OpCode.GetConstant or OpCode.PutConstant or OpCode.UnifyConstant;
}
