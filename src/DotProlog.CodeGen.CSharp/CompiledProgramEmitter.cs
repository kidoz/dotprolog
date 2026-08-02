using System.Globalization;
using System.Text;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

/// <summary>Compiles Prolog source at build time and emits direct-threaded C# predicate blocks.</summary>
internal static class CompiledProgramEmitter
{
    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        out IReadOnlyList<Diagnostic> diagnostics
    ) => Generate(sources, typeName, [], PrologLanguageMode.Extended, out diagnostics);

    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        out IReadOnlyList<Diagnostic> diagnostics
    ) => Generate(sources, typeName, hostBuiltins, PrologLanguageMode.Extended, out diagnostics);

    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        PrologLanguageMode languageMode,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        var engine = new PrologEngine(languageMode)
        {
            Output = TextWriter.Null,
            Error = TextWriter.Null,
            Input = TextReader.Null,
        };

        // This translator consumes the loader's try/retry/trust clause form; first-argument
        // indexing stays a bytecode-VM dispatch strategy.
        engine.Program.EmitFirstArgumentIndexing = false;

        foreach ((var name, var arity) in hostBuiltins)
        {
            engine.Program.Builtins.Register(name, arity, static _ => false);
        }

        var codeStart = engine.Program.CodeLength;
        List<Diagnostic> allDiagnostics = [];
        List<int> initialization = [];
        List<RawPreparationStep> preparation = [];

        foreach ((var name, var source) in sources)
        {
            LoadResult loaded = engine.CompileForGeneratedCode(
                source,
                name,
                address => preparation.Add(new RawPreparationStep(address, SnapshotPredicates(engine.Program, codeStart)))
            );
            allDiagnostics.AddRange(loaded.Diagnostics);
            initialization.AddRange(loaded.InitializationAddresses);
        }

        diagnostics = allDiagnostics;
        if (allDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return string.Empty;
        }

        var model = CompiledModel.Create(engine.Program, codeStart, initialization, preparation);
        return Emit(model, typeName, languageMode);
    }

    private static List<(int Functor, int Entry)> SnapshotPredicates(BytecodeProgram program, int codeStart)
    {
        List<(int Functor, int Entry)> predicates = [];
        for (var functor = 0; functor < program.Symbols.FunctorCount; functor++)
        {
            var entry = program.EntryPointOf(functor);
            if (program.IsUserPredicate(functor) && !program.IsDynamic(functor) && entry >= codeStart)
            {
                predicates.Add((functor, entry));
            }
        }

        return predicates;
    }

    private static string Emit(CompiledModel model, string typeName, PrologLanguageMode languageMode)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"private static class {typeName}");
        text.AppendLine("{");
        text.AppendLine("    internal static int[] Install(global::DotProlog.Compiler.PrologEngine engine)");
        text.AppendLine("    {");
        text.AppendLine("        global::DotProlog.Runtime.BytecodeProgram runtime = engine.Program;");
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"        if (runtime.LanguageMode != global::DotProlog.Runtime.PrologLanguageMode.{languageMode})"
        );
        text.AppendLine("        {");
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"            throw new global::DotProlog.Runtime.PrologException(\"Generated program requires {languageMode} language mode.\");"
        );
        text.AppendLine("        }");
        text.AppendLine();
        text.AppendLine("        global::DotProlog.Runtime.SymbolTable symbols = runtime.Symbols;");
        AppendFunctors(text, model);
        AppendBuiltins(text, model);
        AppendConstants(text, model);
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"        var compiled = new global::DotProlog.Runtime.CompiledProgram(functors, builtins, constants, {model.Instructions.Count});"
        );
        AppendModules(text, model);
        text.AppendLine();

        for (var i = 0; i < model.Instructions.Count; i++)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        compiled.SetTarget({i}, runtime.RegisterCompiledBlock(Block{i}, compiled));"
            );
        }

        foreach (PreparationStep step in model.Preparation)
        {
            text.AppendLine();
            foreach (CompiledPredicate predicate in step.Predicates)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        runtime.DefinePredicate(functors[{predicate.Functor}], compiled.Target({predicate.Entry}));"
                );
            }

            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        if (engine.Machine.Run(compiled.Target({step.Directive})) is global::DotProlog.Runtime.RunResult.Failure)"
            );
            text.AppendLine("        {");
            text.AppendLine("            engine.Output.Write(\"Warning: directive failed.\\n\");");
            text.AppendLine("        }");
        }

        text.AppendLine();
        foreach (CompiledPredicate predicate in model.Predicates)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        runtime.DefinePredicate(functors[{predicate.Functor}], compiled.Target({predicate.Entry}));"
            );
        }

        foreach (CompiledDynamicPredicate predicate in model.DynamicPredicates)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        runtime.DeclareCompiledDynamic(functors[{predicate.Functor}]);"
            );
            foreach (CompiledDynamicClause clause in predicate.Clauses)
            {
                var cells = string.Join(", ", clause.Term.Select(cell => TermCell(cell, "compiled")));
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        runtime.AddCompiledDynamicClause(functors[{predicate.Functor}], compiled.Target({clause.Entry}), [{cells}], {clause.Root});"
                );
            }

            foreach (var alias in predicate.Aliases)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        runtime.AliasPredicate(functors[{alias}], functors[{predicate.Functor}]);"
                );
            }
        }

        var initializers = string.Join(", ", model.Initialization.Select(index => $"compiled.Target({index})"));
        text.AppendLine(CultureInfo.InvariantCulture, $"        return [{initializers}];");
        text.AppendLine("    }");

        if (model.Builtins.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(
                "    private static int RequireBuiltin(global::DotProlog.Runtime.BytecodeProgram runtime, int functor)"
            );
            text.AppendLine("    {");
            text.AppendLine("        if (runtime.Builtins.TryGetId(functor, out int builtin))");
            text.AppendLine("        {");
            text.AppendLine("            return builtin;");
            text.AppendLine("        }");
            text.AppendLine();
            text.AppendLine(
                "        throw new global::DotProlog.Runtime.PrologException($\"Required builtin {runtime.Symbols.DescribeFunctor(functor)} is not registered.\");"
            );
            text.AppendLine("    }");
        }

        for (var i = 0; i < model.Instructions.Count; i++)
        {
            text.AppendLine();
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"    private static bool Block{i}(ref global::DotProlog.Runtime.Machine.CompiledExecution execution, global::DotProlog.Runtime.CompiledProgram program) =>"
            );
            text.AppendLine(CultureInfo.InvariantCulture, $"        {Operation(model, i)};");
        }

        text.AppendLine("}");
        return text.ToString();
    }

    private static void AppendFunctors(StringBuilder text, CompiledModel model)
    {
        text.AppendLine("        int[] functors =");
        text.AppendLine("        [");
        foreach ((var name, var arity) in model.Functors)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"            symbols.InternFunctor({SyntaxFacts.Literal(name)}, {arity}),"
            );
        }

        text.AppendLine("        ];");
    }

    private static void AppendModules(StringBuilder text, CompiledModel model)
    {
        for (var index = 0; index < model.Modules.Count; index++)
        {
            CompiledModule module = model.Modules[index];
            string variable = $"module{index.ToString(CultureInfo.InvariantCulture)}";
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        global::DotProlog.Runtime.ModuleDefinition {variable} = runtime.Modules.Declare({SyntaxFacts.Literal(module.Name)});"
            );
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        {variable}.InterfacePrepared = {module.InterfacePrepared.ToString().ToLowerInvariant()};"
            );
            text.AppendLine(CultureInfo.InvariantCulture, $"        {variable}.Operators.Clear();");
            foreach (PrologOperator op in module.Operators)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {variable}.Operators.Define({op.Priority}, global::DotProlog.Runtime.OperatorType.{op.Type}, {SyntaxFacts.Literal(op.Name)});"
                );
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"        {variable}.CharacterConversions.Clear();");
            foreach ((var input, var output) in module.CharacterConversions)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {variable}.CharacterConversions.Set((char){(int)input}, (char){(int)output});"
                );
            }

            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        {variable}.Flags.SetCharConversion({module.CharConversion.ToString().ToLowerInvariant()});"
            );
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        {variable}.Flags.SetDebug({module.Debug.ToString().ToLowerInvariant()});"
            );
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        {variable}.Flags.SetDoubleQuotes(global::DotProlog.Runtime.DoubleQuotesMode.{module.DoubleQuotes});"
            );
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"        {variable}.Flags.SetUnknown(global::DotProlog.Runtime.UnknownProcedureAction.{module.Unknown});"
            );

            for (var predicateIndex = 0; predicateIndex < module.Predicates.Count; predicateIndex++)
            {
                CompiledModulePredicate predicate = module.Predicates[predicateIndex];
                string predicateVariable = $"{variable}Predicate{predicateIndex.ToString(CultureInfo.InvariantCulture)}";
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        global::DotProlog.Runtime.ModulePredicateDefinition {predicateVariable} = {variable}.Predicate(new global::DotProlog.Runtime.ModulePredicateIndicator({SyntaxFacts.Literal(predicate.Name)}, {predicate.Arity}));"
                );
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {predicateVariable}.Defined = {predicate.Defined.ToString().ToLowerInvariant()};"
                );
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {predicateVariable}.Exported = {predicate.Exported.ToString().ToLowerInvariant()};"
                );
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {predicateVariable}.Dynamic = {predicate.Dynamic.ToString().ToLowerInvariant()};"
                );
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        {predicateVariable}.Multifile = {predicate.Multifile.ToString().ToLowerInvariant()};"
                );
                if (predicate.MetapredicateTemplate is not null)
                {
                    text.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"        {predicateVariable}.MetapredicateTemplate = {SyntaxFacts.Literal(predicate.MetapredicateTemplate)};"
                    );
                }

                foreach (CompiledModuleClause clause in predicate.StaticClauses)
                {
                    var cells = string.Join(", ", clause.Term.Select(cell => TermCell(cell, "compiled")));
                    text.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"        {predicateVariable}.AddStaticClause([{cells}], {clause.Root});"
                    );
                }
            }

            foreach (CompiledModuleImport import in module.Imports)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"        _ = {variable}.TryImport(new global::DotProlog.Runtime.ModulePredicateIndicator({SyntaxFacts.Literal(import.Name)}, {import.Arity}), {SyntaxFacts.Literal(import.From)}, out _);"
                );
            }

            text.AppendLine();
        }
    }

    private static void AppendBuiltins(StringBuilder text, CompiledModel model)
    {
        text.AppendLine("        int[] builtins =");
        text.AppendLine("        [");
        foreach (var functor in model.Builtins)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"            RequireBuiltin(runtime, functors[{functor}]),");
        }

        text.AppendLine("        ];");
    }

    private static void AppendConstants(StringBuilder text, CompiledModel model)
    {
        text.AppendLine("        global::DotProlog.Runtime.Cell[] constants =");
        text.AppendLine("        [");
        foreach (CompiledConstant constant in model.Constants)
        {
            var expression = constant.Tag switch
            {
                CellTag.Atom => $"global::DotProlog.Runtime.Cell.Atom(symbols.InternAtom({SyntaxFacts.Literal(constant.Text!)}))",
                CellTag.Integer =>
                    $"global::DotProlog.Runtime.Cell.Integer60({constant.Integer.ToString(CultureInfo.InvariantCulture)}L)",
                CellTag.Float =>
                    $"global::DotProlog.Runtime.Cell.Float(symbols.InternFloat({constant.Float.ToString("R", CultureInfo.InvariantCulture)}))",
                _ => throw new InvalidOperationException($"Unsupported generated constant {constant.Tag}."),
            };
            text.AppendLine(CultureInfo.InvariantCulture, $"            {expression},");
        }

        text.AppendLine("        ];");
    }

    private static string Operation(CompiledModel model, int index)
    {
        CompiledInstruction instruction = model.Instructions[index];
        var next = Target(model, instruction.NextAddress);
        var a = instruction.First.ToString(CultureInfo.InvariantCulture);
        var b = instruction.Second.ToString(CultureInfo.InvariantCulture);

        return instruction.OpCode switch
        {
            OpCode.Stop => "execution.Stop()",
            OpCode.Allocate => $"execution.Allocate({a}, {next})",
            OpCode.Deallocate => $"execution.Deallocate({next})",
            OpCode.Call => $"execution.Call(program.Functor({instruction.FirstReference}), {b}, {next})",
            OpCode.Execute => $"execution.Execute(program.Functor({instruction.FirstReference}), {b})",
            OpCode.CallBuiltin => $"execution.CallBuiltin(program.Builtin({instruction.FirstReference}), {b}, {next})",
            OpCode.Proceed => "execution.Proceed()",
            OpCode.Cut => $"execution.Cut({next})",
            OpCode.TryBranch => $"execution.TryBranch({a}, {Target(model, instruction.Second)}, {next})",
            OpCode.MarkBarrier => $"execution.MarkBarrier({a}, {next})",
            OpCode.Jump => $"execution.Jump({Target(model, instruction.First)})",
            OpCode.CutTo => $"execution.CutTo({a}, {next})",
            OpCode.SoftCut => $"execution.SoftCut({a}, {next})",
            OpCode.MetaCall => $"execution.MetaCall({next})",
            OpCode.PushCatch => $"execution.PushCatch({a}, {Target(model, instruction.Second)}, {next})",
            OpCode.PopCatch => $"execution.PopCatch({a}, {Target(model, instruction.Second)}, {next})",
            OpCode.ReactivateCatch => $"execution.ReactivateCatch({a}, {next})",
            OpCode.TryMeElse => $"execution.TryMeElse({Target(model, instruction.First)}, {next})",
            OpCode.RetryMeElse => $"execution.RetryMeElse({Target(model, instruction.First)}, {next})",
            OpCode.TrustMe => $"execution.TrustMe({next})",
            OpCode.GetVariable => $"execution.GetVariable({a}, {b}, {next})",
            OpCode.GetValue => $"execution.GetValue({a}, {b}, {next})",
            OpCode.GetConstant => $"execution.GetConstant(program.Constant({instruction.FirstReference}), {b}, {next})",
            OpCode.GetStructureArgument =>
                $"execution.GetStructureArgument(program.Functor({instruction.FirstReference}), {b}, {next})",
            OpCode.GetStructureSlot => $"execution.GetStructureSlot(program.Functor({instruction.FirstReference}), {b}, {next})",
            OpCode.UnifyVariable => $"execution.UnifyVariable({a}, {next})",
            OpCode.UnifyValue => $"execution.UnifyValue({a}, {next})",
            OpCode.UnifyConstant => $"execution.UnifyConstant(program.Constant({instruction.FirstReference}), {next})",
            OpCode.PutVariable => $"execution.PutVariable({a}, {b}, {next})",
            OpCode.InitVariable => $"execution.InitVariable({a}, {next})",
            OpCode.PutValue => $"execution.PutValue({a}, {b}, {next})",
            OpCode.PutConstant => $"execution.PutConstant(program.Constant({instruction.FirstReference}), {b}, {next})",
            OpCode.PutStructureArgument =>
                $"execution.PutStructureArgument(program.Functor({instruction.FirstReference}), {b}, {next})",
            OpCode.PutStructureSlot => $"execution.PutStructureSlot(program.Functor({instruction.FirstReference}), {b}, {next})",
            OpCode.EnterDynamic => $"execution.EnterDynamic(program.Functor({instruction.FirstReference}))",
            OpCode.Fail => "execution.Fail()",
            _ => throw new InvalidOperationException($"Opcode {instruction.OpCode} cannot be emitted as compiled C#."),
        };
    }

    private static string Target(CompiledModel model, int address) =>
        model.InstructionByAddress.TryGetValue(address, out var target)
            ? $"program.Target({target})"
            : address.ToString(CultureInfo.InvariantCulture);

    private static string TermCell(CompiledTermCell cell, string program) =>
        cell.Tag switch
        {
            CellTag.Reference => $"global::DotProlog.Runtime.Cell.Reference({cell.Value})",
            CellTag.Structure => $"global::DotProlog.Runtime.Cell.Structure({cell.Value})",
            CellTag.Functor => $"global::DotProlog.Runtime.Cell.Functor({program}.Functor({cell.Value}))",
            CellTag.Atom or CellTag.Integer or CellTag.Float => $"{program}.Constant({cell.Value})",
            _ => throw new InvalidOperationException($"Term cell tag {cell.Tag} cannot be generated."),
        };

    private sealed record CompiledPredicate(int Functor, int Entry);

    private sealed record RawPreparationStep(int Directive, List<(int Functor, int Entry)> Predicates);

    private sealed record PreparationStep(int Directive, List<CompiledPredicate> Predicates);

    private sealed record CompiledConstant(CellTag Tag, string? Text, long Integer, double Float);

    private sealed record CompiledTermCell(CellTag Tag, int Value);

    private sealed record CompiledDynamicClause(int Entry, int Root, List<CompiledTermCell> Term);

    private sealed record CompiledDynamicPredicate(int Functor, List<int> Aliases, List<CompiledDynamicClause> Clauses);

    private sealed record CompiledModuleImport(string Name, int Arity, string From);

    private sealed record CompiledModuleClause(int Root, List<CompiledTermCell> Term);

    private sealed record CompiledModulePredicate(
        string Name,
        int Arity,
        bool Defined,
        bool Exported,
        bool Dynamic,
        bool Multifile,
        string? MetapredicateTemplate,
        List<CompiledModuleClause> StaticClauses
    );

    private sealed record CompiledModule(
        string Name,
        bool InterfacePrepared,
        List<PrologOperator> Operators,
        List<(char Input, char Output)> CharacterConversions,
        bool CharConversion,
        bool Debug,
        DoubleQuotesMode DoubleQuotes,
        UnknownProcedureAction Unknown,
        List<CompiledModulePredicate> Predicates,
        List<CompiledModuleImport> Imports
    );

    private sealed class CompiledInstruction
    {
        internal required int Address { get; init; }
        internal required int NextAddress { get; init; }
        internal required OpCode OpCode { get; init; }
        internal int First { get; init; }
        internal int Second { get; init; }
        internal int FirstReference { get; set; } = -1;
    }

    private sealed class CompiledModel
    {
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

        internal static CompiledModel Create(
            BytecodeProgram program,
            int codeStart,
            IReadOnlyList<int> initialization,
            IReadOnlyList<RawPreparationStep> preparation
        )
        {
            var model = new CompiledModel();
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
                            CellTag.Atom or CellTag.Integer or CellTag.Float => AddTermConstant(
                                program,
                                model,
                                termConstants,
                                cell
                            ),
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
            CompiledModel model,
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
                    CellTag.Atom or CellTag.Integer or CellTag.Float => AddTermConstant(program, model, termConstants, cell),
                    _ => throw new InvalidOperationException($"Static module term cell {cell.Tag} cannot be generated."),
                };
                term.Add(new CompiledTermCell(cell.Tag, value));
            }

            return term;
        }

        private static int AddTermConstant(
            BytecodeProgram program,
            CompiledModel model,
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
            CompiledModel model,
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
                CellTag.Atom => new(constant.Tag, program.Symbols.AtomName(constant.Index), 0, 0),
                CellTag.Integer => new(constant.Tag, null, constant.Integer, 0),
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
    }
}
