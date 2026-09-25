using System.Globalization;
using DotProlog.Runtime;

namespace DotProlog.Compiler;

internal sealed class CompiledProgramModel
{
    internal PrologLanguageMode LanguageMode { get; set; }
    internal DoubleQuotesMode InitialDoubleQuotes { get; set; }

    internal List<(string Name, int Arity)> Functors { get; } = [];
    internal List<int> Builtins { get; } = [];
    internal List<CompiledConstant> Constants { get; } = [];
    internal List<CompiledInstruction> Instructions { get; } = [];
    internal Dictionary<int, int> InstructionByAddress { get; } = [];
    internal List<CompiledPredicate> Predicates { get; } = [];
    internal List<int> Initialization { get; } = [];
    internal List<PreparationStep> Preparation { get; } = [];
    internal List<CompiledDynamicPredicate> DynamicPredicates { get; } = [];
    internal List<CompiledModule> Modules { get; } = [];

    internal static CompiledProgramModel Create(
        BytecodeProgram program,
        int codeStart,
        IReadOnlyList<int> initialization,
        IReadOnlyList<RawPreparationStep> preparation
    )
    {
        var model = new CompiledProgramModel
        {
            LanguageMode = program.LanguageMode,
            InitialDoubleQuotes = program.InitialDoubleQuotes,
        };
        Dictionary<int, int> functors = [];
        Dictionary<int, int> builtins = [];
        Dictionary<int, int> constants = [];
        Dictionary<Cell, int> termConstants = [];
        var code = program.Code;

        for (var address = codeStart; address < program.CodeLength; )
        {
            var opCode = (OpCode)code[address];
            var operands = OperandCount(opCode);
            var instruction = new CompiledInstruction
            {
                Address = address,
                NextAddress = address + 1 + operands,
                OpCode = opCode,
                First = operands > 0 ? code[address + 1] : 0,
                Second = operands > 1 ? code[address + 2] : 0,
            };
            model.InstructionByAddress[address] = model.Instructions.Count;
            model.Instructions.Add(instruction);
            address = instruction.NextAddress;
        }

        foreach (CompiledInstruction instruction in model.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Call:
                case OpCode.Execute:
                case OpCode.GetStructureArgument:
                case OpCode.GetStructureSlot:
                case OpCode.PutStructureArgument:
                case OpCode.PutStructureSlot:
                case OpCode.EnterDynamic:
                    instruction.FirstReference = AddFunctor(program, model, functors, instruction.First);
                    break;

                case OpCode.CallBuiltin:
                {
                    var display = program.Builtins.NameOf(instruction.First);
                    var slash = display.LastIndexOf('/');
                    var name = display[..slash];
                    var arity = int.Parse(display.AsSpan(slash + 1), CultureInfo.InvariantCulture);
                    var functorId = program.Symbols.InternFunctor(name, arity);
                    var functor = AddFunctor(program, model, functors, functorId);
                    if (!builtins.TryGetValue(instruction.First, out var reference))
                    {
                        reference = model.Builtins.Count;
                        builtins[instruction.First] = reference;
                        model.Builtins.Add(functor);
                    }

                    instruction.FirstReference = reference;
                    break;
                }

                case OpCode.GetConstant:
                case OpCode.UnifyConstant:
                case OpCode.PutConstant:
                    if (!constants.TryGetValue(instruction.First, out var constant))
                    {
                        constant = model.Constants.Count;
                        constants[instruction.First] = constant;
                        model.Constants.Add(DescribeConstant(program, program.Constants[instruction.First]));
                    }

                    instruction.FirstReference = constant;
                    break;
            }
        }

        for (var functorId = 0; functorId < program.Symbols.FunctorCount; functorId++)
        {
            var entry = program.EntryPointOf(functorId);
            if (
                program.IsUserPredicate(functorId)
                && !program.IsDynamic(functorId)
                && model.InstructionByAddress.TryGetValue(entry, out var compiledEntry)
            )
            {
                model.Predicates.Add(new CompiledPredicate(AddFunctor(program, model, functors, functorId), compiledEntry));
            }
        }

        foreach (var address in initialization)
        {
            if (model.InstructionByAddress.TryGetValue(address, out var compiledEntry))
            {
                model.Initialization.Add(compiledEntry);
            }
        }

        foreach (RawPreparationStep raw in preparation)
        {
            if (!model.InstructionByAddress.TryGetValue(raw.Directive, out var directive))
            {
                continue;
            }

            List<CompiledPredicate> predicates = [];
            foreach ((var functor, var entry) in raw.Predicates)
            {
                if (model.InstructionByAddress.TryGetValue(entry, out var compiledEntry))
                {
                    predicates.Add(new CompiledPredicate(AddFunctor(program, model, functors, functor), compiledEntry));
                }
            }

            model.Preparation.Add(new PreparationStep(directive, predicates));
        }

        HashSet<DynamicPredicate> seenDynamic = new(ReferenceEqualityComparer.Instance);
        foreach ((_, DynamicPredicate predicate) in program.DynamicPredicates)
        {
            if (!seenDynamic.Add(predicate))
            {
                continue;
            }

            var functor = AddFunctor(program, model, functors, predicate.FunctorId);
            List<int> aliases = [];
            foreach ((var candidateFunctor, DynamicPredicate candidate) in program.DynamicPredicates)
            {
                if (candidateFunctor != predicate.FunctorId && ReferenceEquals(candidate, predicate))
                {
                    aliases.Add(AddFunctor(program, model, functors, candidateFunctor));
                }
            }

            List<CompiledDynamicClause> clauses = [];
            for (DynamicClause? clause = predicate.First; clause is not null; clause = clause.Next)
            {
                if (!model.InstructionByAddress.TryGetValue(clause.CodeAddress, out var entry))
                {
                    continue;
                }

                List<CompiledTermCell> term = [];
                foreach (Cell cell in clause.Term.Cells)
                {
                    var value = cell.Tag switch
                    {
                        CellTag.Reference or CellTag.Structure => cell.Index,
                        CellTag.Functor => AddFunctor(program, model, functors, cell.Index),
                        CellTag.Atom or CellTag.Integer or CellTag.BigInteger or CellTag.Float or CellTag.String =>
                            AddTermConstant(program, model, termConstants, cell),
                        _ => throw new InvalidOperationException($"Dynamic term cell {cell.Tag} cannot be generated."),
                    };
                    term.Add(new CompiledTermCell(cell.Tag, value));
                }

                clauses.Add(new CompiledDynamicClause(entry, clause.TermRoot, term));
            }

            model.DynamicPredicates.Add(new CompiledDynamicPredicate(functor, aliases, clauses));
        }

        foreach (ModuleDefinition module in program.Modules.Definitions)
        {
            List<CompiledModulePredicate> predicates = [];
            foreach (ModulePredicateDefinition predicate in module.Predicates)
            {
                string compiledName =
                    module.Name == "user" ? predicate.Indicator.Name : $"{module.Name}:{predicate.Indicator.Name}";
                int functor = program.Symbols.InternFunctor(compiledName, predicate.Indicator.Arity);
                if (predicate.Defined && !program.IsUserPredicate(functor))
                {
                    continue;
                }

                predicates.Add(
                    new CompiledModulePredicate(
                        predicate.Indicator.Name,
                        predicate.Indicator.Arity,
                        predicate.Defined,
                        predicate.Exported,
                        predicate.Dynamic,
                        predicate.Multifile,
                        predicate.MetapredicateTemplate,
                        [
                            .. predicate.StaticClauses.Select(clause => new CompiledModuleClause(
                                clause.Root,
                                DescribeTerm(program, model, functors, termConstants, clause.Term.Cells)
                            )),
                        ]
                    )
                );
            }

            List<CompiledModuleImport> imports =
            [
                .. module.Imports.Select(import => new CompiledModuleImport(import.Key.Name, import.Key.Arity, import.Value)),
            ];
            if (module.Name == "user" && predicates.Count == 0 && imports.Count == 0)
            {
                continue;
            }

            model.Modules.Add(
                new CompiledModule(
                    module.Name,
                    module.InterfacePrepared,
                    [.. module.Operators.All()],
                    [.. module.CharacterConversions.All()],
                    module.Flags.CharConversion,
                    module.Flags.Debug,
                    module.Flags.DoubleQuotes,
                    module.Flags.Unknown,
                    predicates,
                    imports
                )
            );
        }

        return model;
    }

    private static List<CompiledTermCell> DescribeTerm(
        BytecodeProgram program,
        CompiledProgramModel model,
        Dictionary<int, int> functors,
        Dictionary<Cell, int> termConstants,
        ReadOnlySpan<Cell> cells
    )
    {
        List<CompiledTermCell> term = [];
        foreach (Cell cell in cells)
        {
            var value = cell.Tag switch
            {
                CellTag.Reference or CellTag.Structure => cell.Index,
                CellTag.Functor => AddFunctor(program, model, functors, cell.Index),
                CellTag.Atom or CellTag.Integer or CellTag.BigInteger or CellTag.Rational or CellTag.Float or CellTag.String =>
                    AddTermConstant(program, model, termConstants, cell),
                _ => throw new InvalidOperationException($"Static module term cell {cell.Tag} cannot be generated."),
            };
            term.Add(new CompiledTermCell(cell.Tag, value));
        }

        return term;
    }

    private static int AddTermConstant(
        BytecodeProgram program,
        CompiledProgramModel model,
        Dictionary<Cell, int> constants,
        Cell cell
    )
    {
        if (constants.TryGetValue(cell, out var reference))
        {
            return reference;
        }

        reference = model.Constants.Count;
        constants[cell] = reference;
        model.Constants.Add(DescribeConstant(program, cell));
        return reference;
    }

    private static int AddFunctor(
        BytecodeProgram program,
        CompiledProgramModel model,
        Dictionary<int, int> references,
        int functorId
    )
    {
        if (references.TryGetValue(functorId, out var reference))
        {
            return reference;
        }

        Functor functor = program.Symbols.GetFunctor(functorId);
        reference = model.Functors.Count;
        references[functorId] = reference;
        model.Functors.Add((program.Symbols.AtomName(functor.NameAtom), functor.Arity));
        return reference;
    }

    private static CompiledConstant DescribeConstant(BytecodeProgram program, Cell constant) =>
        constant.Tag switch
        {
            CellTag.Atom or CellTag.String => new(constant.Tag, program.Symbols.AtomName(constant.Index), 0, 0),
            CellTag.Integer => new(constant.Tag, null, constant.Integer, 0),
            CellTag.BigInteger => new(
                constant.Tag,
                program.Symbols.GetBig(constant.Index).ToString(CultureInfo.InvariantCulture),
                0,
                0
            ),
            CellTag.Rational => new(constant.Tag, RationalText(program, constant.Index), 0, 0),
            CellTag.Float => new(constant.Tag, null, 0, program.Symbols.GetFloat(constant.Index)),
            _ => throw new InvalidOperationException($"Constant tag {constant.Tag} cannot be generated."),
        };

    private static int OperandCount(OpCode opCode) =>
        opCode switch
        {
            OpCode.Stop
            or OpCode.Deallocate
            or OpCode.Proceed
            or OpCode.Cut
            or OpCode.MetaCall
            or OpCode.TrustMe
            or OpCode.NextClause
            or OpCode.RedoBuiltin
            or OpCode.Fail => 0,
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
            or OpCode.EnterDynamic => 1,
            _ => 2,
        };

    private static string RationalText(BytecodeProgram program, int rationalId)
    {
        (System.Numerics.BigInteger numerator, System.Numerics.BigInteger denominator) = program.Symbols.GetRational(rationalId);
        return string.Create(CultureInfo.InvariantCulture, $"{numerator}r{denominator}");
    }
}
