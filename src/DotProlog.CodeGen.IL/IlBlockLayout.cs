using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.CodeGen.IL;

/// <summary>Groups straight-line operations without changing the machine's resumption contract.</summary>
internal sealed class IlBlockLayout
{
    internal List<int> Starts { get; } = [];
    internal int[] BlockByInstruction { get; }

    private IlBlockLayout(int instructionCount) => BlockByInstruction = new int[instructionCount];

    internal static IlBlockLayout Create(CompiledProgramModel model, bool fuseBlocks = true)
    {
        var layout = new IlBlockLayout(model.Instructions.Count);
        var starts = new bool[model.Instructions.Count];
        void Entry(int instruction) => starts[instruction] = true;
        void Address(int address)
        {
            if (model.InstructionByAddress.TryGetValue(address, out var instruction))
            {
                Entry(instruction);
            }
        }

        foreach (var predicate in model.Predicates)
        {
            Entry(predicate.Entry);
        }
        foreach (var step in model.Preparation)
        {
            Entry(step.Directive);
            foreach (var predicate in step.Predicates)
            {
                Entry(predicate.Entry);
            }
        }
        foreach (var predicate in model.DynamicPredicates)
        {
            foreach (var clause in predicate.Clauses)
            {
                Entry(clause.Entry);
            }
        }
        foreach (var entry in model.Initialization)
        {
            Entry(entry);
        }
        foreach (var index in model.StaticIndexes)
        {
            foreach (var entry in index.Entries)
            {
                Entry(entry);
            }
        }
        foreach (var instruction in model.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Jump:
                case OpCode.TryMeElse:
                case OpCode.RetryMeElse:
                    Address(instruction.First);
                    break;
                case OpCode.TryBranch:
                case OpCode.PushCatch:
                case OpCode.PopCatch:
                    Address(instruction.Second);
                    break;
            }
        }

        for (var index = 0; index < model.Instructions.Count; index++)
        {
            // CompiledExecution captures the entered target for wake-up resumption. Operations
            // that can wake, call, redirect control, or invoke a builtin must remain singleton
            // blocks, with a separately addressable continuation. Failed unification exits the
            // fused block immediately so the existing dispatcher alone performs backtracking.
            if (
                !fuseBlocks
                || index == 0
                || starts[index]
                || !CanFuse(model.Instructions[index - 1].OpCode)
                || !CanFuse(model.Instructions[index].OpCode)
            )
            {
                layout.Starts.Add(index);
            }
            layout.BlockByInstruction[index] = layout.Starts.Count - 1;
        }
        return layout;
    }

    private static bool CanFuse(OpCode op) =>
        op
            is OpCode.Allocate
                or OpCode.Deallocate
                or OpCode.TryMeElse
                or OpCode.RetryMeElse
                or OpCode.TrustMe
                or OpCode.GetVariable
                or OpCode.GetValue
                or OpCode.GetConstant
                or OpCode.GetStructureArgument
                or OpCode.GetStructureSlot
                or OpCode.UnifyVariable
                or OpCode.UnifyValue
                or OpCode.UnifyConstant
                or OpCode.PutVariable
                or OpCode.InitVariable
                or OpCode.PutValue
                or OpCode.PutConstant
                or OpCode.PutStructureArgument
                or OpCode.PutStructureSlot;
}
