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
    ) => Generate(sources, typeName, [], PrologLanguageMode.Modern, out diagnostics);

    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        out IReadOnlyList<Diagnostic> diagnostics
    ) => Generate(sources, typeName, hostBuiltins, PrologLanguageMode.Modern, out diagnostics);

    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        PrologLanguageMode languageMode,
        out IReadOnlyList<Diagnostic> diagnostics
    ) => Generate(sources, typeName, hostBuiltins, languageMode, PrologFlagOverrides.None, out diagnostics);

    internal static string Generate(
        IReadOnlyList<(string Name, string Text)> sources,
        string typeName,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        PrologLanguageMode languageMode,
        PrologFlagOverrides flagOverrides,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        CompiledProgramModel? model = CompiledProgramBuilder.Compile(
            sources,
            hostBuiltins,
            languageMode,
            flagOverrides,
            out diagnostics
        );
        return model is null ? string.Empty : Emit(model, typeName, languageMode, model.InitialDoubleQuotes);
    }

    private static string Emit(
        CompiledProgramModel model,
        string typeName,
        PrologLanguageMode languageMode,
        DoubleQuotesMode initialDoubleQuotes
    )
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
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"        if (runtime.InitialDoubleQuotes != global::DotProlog.Runtime.DoubleQuotesMode.{initialDoubleQuotes})"
        );
        text.AppendLine("        {");
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"            throw new global::DotProlog.Runtime.PrologException(\"Generated program requires double_quotes to start at {FlagName(initialDoubleQuotes)}.\");"
        );
        text.AppendLine("        }");
        text.AppendLine();
        text.AppendLine("        global::DotProlog.Runtime.SymbolTable symbols = runtime.Symbols;");
        AppendFunctors(text, model);
        AppendBuiltins(text, model);
        text.AppendLine("        global::DotProlog.Runtime.Cell[] constants = CreateConstants(symbols);");
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

        if (model.Preparation.Count > 0)
        {
            // The sources were read as load units, whose double_quotes changes do not outlive the
            // unit. Replaying their directives here must not leak past Install either.
            text.AppendLine();
            text.AppendLine(
                "        global::DotProlog.Runtime.DoubleQuotesMode enteringDoubleQuotes = runtime.Flags.DoubleQuotes;"
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

        if (model.Preparation.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("        runtime.Flags.SetDoubleQuotes(enteringDoubleQuotes);");
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

        // Keep constant construction out of large registration methods. The expanded strict
        // corpus exposed incorrect floating constants in a monolithic NativeAOT installation.
        text.AppendLine();
        text.AppendLine(
            "    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]"
        );
        text.AppendLine(
            "    private static global::DotProlog.Runtime.Cell[] CreateConstants(global::DotProlog.Runtime.SymbolTable symbols)"
        );
        text.AppendLine("    {");
        AppendConstants(text, model);
        text.AppendLine("        return constants;");
        text.AppendLine("    }");
        text.AppendLine("}");
        return text.ToString();
    }

    /// <summary>The Prolog spelling of a <c>double_quotes</c> value, for diagnostics.</summary>
    private static string FlagName(DoubleQuotesMode mode) =>
        mode switch
        {
            DoubleQuotesMode.Chars => "chars",
            DoubleQuotesMode.Atom => "atom",
            DoubleQuotesMode.String => "string",
            _ => "codes",
        };

    private static void AppendFunctors(StringBuilder text, CompiledProgramModel model)
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

    private static void AppendModules(StringBuilder text, CompiledProgramModel model)
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
                text.AppendLine(CultureInfo.InvariantCulture, $"        {variable}.CharacterConversions.Set({input}, {output});");
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

    private static void AppendBuiltins(StringBuilder text, CompiledProgramModel model)
    {
        text.AppendLine("        int[] builtins =");
        text.AppendLine("        [");
        foreach (var functor in model.Builtins)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"            RequireBuiltin(runtime, functors[{functor}]),");
        }

        text.AppendLine("        ];");
    }

    private static void AppendConstants(StringBuilder text, CompiledProgramModel model)
    {
        text.AppendLine("        global::DotProlog.Runtime.Cell[] constants =");
        text.AppendLine("        [");
        foreach (CompiledConstant constant in model.Constants)
        {
            var expression = constant.Tag switch
            {
                CellTag.Atom => $"global::DotProlog.Runtime.Cell.Atom(symbols.InternAtom({SyntaxFacts.Literal(constant.Text!)}))",
                CellTag.String =>
                    $"global::DotProlog.Runtime.Cell.String(symbols.InternAtom({SyntaxFacts.Literal(constant.Text!)}))",
                CellTag.Integer =>
                    $"global::DotProlog.Runtime.Cell.Integer60({constant.Integer.ToString(CultureInfo.InvariantCulture)}L)",
                CellTag.BigInteger =>
                    $"global::DotProlog.Runtime.Cell.Big(symbols.InternBig(global::System.Numerics.BigInteger.Parse({SyntaxFacts.Literal(constant.Text!)}, global::System.Globalization.CultureInfo.InvariantCulture)))",
                CellTag.Rational => RationalExpression(constant.Text!),
                CellTag.Float =>
                    $"global::DotProlog.Runtime.Cell.Float(symbols.InternFloat({constant.Float.ToString("R", CultureInfo.InvariantCulture)}))",
                _ => throw new InvalidOperationException($"Unsupported generated constant {constant.Tag}."),
            };
            text.AppendLine(CultureInfo.InvariantCulture, $"            {expression},");
        }

        text.AppendLine("        ];");
    }

    private static string Operation(CompiledProgramModel model, int index)
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

    private static string Target(CompiledProgramModel model, int address) =>
        model.InstructionByAddress.TryGetValue(address, out var target)
            ? $"program.Target({target})"
            : address.ToString(CultureInfo.InvariantCulture);

    private static string TermCell(CompiledTermCell cell, string program) =>
        cell.Tag switch
        {
            CellTag.Reference => $"global::DotProlog.Runtime.Cell.Reference({cell.Value})",
            CellTag.Structure => $"global::DotProlog.Runtime.Cell.Structure({cell.Value})",
            CellTag.Functor => $"global::DotProlog.Runtime.Cell.Functor({program}.Functor({cell.Value}))",
            CellTag.Atom or CellTag.Integer or CellTag.BigInteger or CellTag.Float or CellTag.String =>
                $"{program}.Constant({cell.Value})",
            _ => throw new InvalidOperationException($"Term cell tag {cell.Tag} cannot be generated."),
        };

    private static string RationalExpression(string text)
    {
        var separator = text.IndexOf('r', StringComparison.Ordinal);
        var numerator = SyntaxFacts.Literal(text[..separator]);
        var denominator = SyntaxFacts.Literal(text[(separator + 1)..]);
        return "global::DotProlog.Runtime.Cell.Rational(symbols.InternRational("
            + $"global::System.Numerics.BigInteger.Parse({numerator}, global::System.Globalization.CultureInfo.InvariantCulture), "
            + $"global::System.Numerics.BigInteger.Parse({denominator}, global::System.Globalization.CultureInfo.InvariantCulture)))";
    }
}
