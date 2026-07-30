using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

/// <summary>
/// Reads a <c>.dpli</c> contract, which declares the .NET surface of a Prolog module.
/// </summary>
/// <remarks>
/// The contract is written in Prolog syntax so the existing reader parses it and there is no second
/// grammar to maintain. It is a separate file from the <c>.pl</c> so the Prolog source stays plain
/// ISO and loadable by other systems — the same split as DotPython's <c>.pyi</c> beside its <c>.py</c>.
/// <code>
/// :- clr_module('Pricing').
/// :- clr_namespace('Contoso.Pricing').
/// :- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
/// </code>
/// </remarks>
public static class ContractReader
{
    /// <summary>Reads a contract from source text.</summary>
    /// <param name="text">The contract's contents.</param>
    /// <param name="defaultNamespace">Namespace used when the contract does not declare one.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    public static ContractReadResult Read(string text, string defaultNamespace, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(defaultNamespace);

        ParseResult parsed = TermReader.ReadProgram(text, fileName);
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics];

        if (!parsed.Success)
        {
            return new ContractReadResult(null, diagnostics);
        }

        string? typeName = null;
        string? declaredNamespace = null;
        List<ContractExport> exports = [];

        foreach (SyntaxTerm clause in parsed.Clauses)
        {
            if (clause is not CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
                Report(
                    diagnostics,
                    CodeGenDiagnosticIds.UnknownDirective,
                    "A contract holds directives only.",
                    clause.Span,
                    fileName
                );
                continue;
            }

            switch (directive.Arguments[0])
            {
                case CompoundTerm { Name: "clr_module", Arity: 1 } module when module.Arguments[0] is AtomTerm name:
                    if (!SyntaxFacts.IsIdentifier(name.Name))
                    {
                        Report(
                            diagnostics,
                            CodeGenDiagnosticIds.InvalidModuleTypeName,
                            $"'{name.Name}' is not a valid C# identifier, so it cannot name the generated type.",
                            module.Span,
                            fileName
                        );
                    }

                    typeName = name.Name;
                    break;

                case CompoundTerm { Name: "clr_namespace", Arity: 1 } space when space.Arguments[0] is AtomTerm name:
                    if (!SyntaxFacts.IsDottedIdentifierSequence(name.Name))
                    {
                        Report(
                            diagnostics,
                            CodeGenDiagnosticIds.InvalidNamespace,
                            $"'{name.Name}' is not a dot-separated sequence of C# identifiers, so it cannot name the namespace.",
                            space.Span,
                            fileName
                        );
                    }

                    declaredNamespace = name.Name;
                    break;

                case CompoundTerm { Name: "clr_export", Arity: 3 or 4 } export:
                {
                    ContractExport? read = ReadExport(export, diagnostics, fileName);
                    if (read is not null)
                    {
                        exports.Add(read);
                    }

                    break;
                }

                default:
                    Report(
                        diagnostics,
                        CodeGenDiagnosticIds.UnknownDirective,
                        "Expected clr_module/1, clr_namespace/1, or clr_export/3.",
                        directive.Span,
                        fileName
                    );
                    break;
            }
        }

        if (typeName is null)
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.MissingModuleDeclaration,
                "The contract must declare its CLR type name with clr_module/1.",
                SourceSpan.None,
                fileName
            );

            return new ContractReadResult(null, diagnostics);
        }

        RejectClashingSignatures(exports, diagnostics, fileName);

        var contract = new ModuleContract(typeName, declaredNamespace ?? defaultNamespace, exports);
        return new ContractReadResult(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? null : contract,
            diagnostics
        );
    }

    /// <summary>
    /// Rejects two exports that would generate the same method name and parameter types.
    /// </summary>
    /// <remarks>
    /// A predicate exported in more than one mode gets one method per mode, and overloading sorts most
    /// of them out. Two modes with the same input types do clash, though — <c>append(+,-,+)</c> and
    /// <c>append(-,+,+)</c> both take two lists — and the fix is to name one of them with
    /// <c>clr_export/4</c>. Catching it here makes that a contract error rather than a compile error
    /// in code the author never wrote.
    /// </remarks>
    private static void RejectClashingSignatures(List<ContractExport> exports, List<Diagnostic> diagnostics, string? fileName)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ContractExport export in exports)
        {
            string name = export.ClrName ?? export.PredicateName;
            string signature = $"{name}({string.Join(",", export.Inputs.Select(input => input.Type.ClrTypeName))})";

            if (seen.Add(signature))
            {
                continue;
            }

            Report(
                diagnostics,
                CodeGenDiagnosticIds.DuplicateMethodSignature,
                $"{export.PredicateName}/{export.Arity} would generate a method that another export already generates. "
                    + "Give one of them a name with clr_export/4.",
                SourceSpan.None,
                fileName
            );
        }
    }

    private static ContractExport? ReadExport(CompoundTerm export, List<Diagnostic> diagnostics, string? fileName)
    {
        if (
            export.Arguments[0] is not CompoundTerm { Name: "/", Arity: 2 } indicator
            || indicator.Arguments[0] is not AtomTerm name
            || indicator.Arguments[1] is not IntegerTerm arity
        )
        {
            Report(diagnostics, CodeGenDiagnosticIds.InvalidPredicateIndicator, "Expected Name/Arity.", export.Span, fileName);
            return null;
        }

        if (!SyntaxFacts.MapsToIdentifier(name.Name))
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.NameDoesNotMapToIdentifier,
                $"'{name.Name}' does not map to a C# identifier; only letters, digits, and underscores do.",
                indicator.Span,
                fileName
            );

            return null;
        }

        if (
            export.Arguments[1] is not AtomTerm determinismTerm
            || !TryReadDeterminism(determinismTerm.Name, out Determinism determinism)
        )
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.UnknownDeterminism,
                "Expected det, semidet, multi, or nondet.",
                export.Arguments[1].Span,
                fileName
            );

            return null;
        }

        List<ContractArgument> arguments = [];
        SyntaxTerm list = export.Arguments[2];

        while (list is CompoundTerm { Name: ".", Arity: 2 } pair)
        {
            ContractArgument? argument = ReadArgument(pair.Arguments[0], diagnostics, fileName);
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
            list = pair.Arguments[1];
        }

        if (list is not AtomTerm { Name: "[]" })
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.InvalidArgument,
                "Expected a list of argument specifications.",
                list.Span,
                fileName
            );
            return null;
        }

        if (arguments.Count != arity.Value)
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.ArityMismatch,
                $"{name.Name}/{arity.Value} was given {arguments.Count} argument specifications.",
                export.Span,
                fileName
            );

            return null;
        }

        if (determinism == Determinism.Det && arguments.TrueForAll(argument => argument.Mode == ArgumentMode.In))
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.DeterministicExportNeedsOutput,
                $"{name.Name}/{arity.Value} is det with no outputs; declare it semidet instead.",
                export.Span,
                fileName
            );

            return null;
        }

        // ADR 0006 defines no signature for multi with no outputs; nondet with none streams units.
        if (determinism == Determinism.Multi && arguments.TrueForAll(argument => argument.Mode == ArgumentMode.In))
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.MultiExportNeedsOutput,
                $"{name.Name}/{arity.Value} is multi with no outputs; declare it nondet or semidet instead.",
                export.Span,
                fileName
            );

            return null;
        }

        string? clrName = null;
        if (export.Arity == 4)
        {
            if (export.Arguments[3] is not AtomTerm alias)
            {
                Report(
                    diagnostics,
                    CodeGenDiagnosticIds.InvalidClrName,
                    "The fourth argument of clr_export/4 must be an atom naming the generated method.",
                    export.Arguments[3].Span,
                    fileName
                );

                return null;
            }

            // The alias is emitted verbatim as the method name, so it has to be legal as written.
            if (!SyntaxFacts.IsIdentifier(alias.Name))
            {
                Report(
                    diagnostics,
                    CodeGenDiagnosticIds.InvalidClrName,
                    $"'{alias.Name}' is not a valid C# identifier, so it cannot name the generated method.",
                    export.Arguments[3].Span,
                    fileName
                );

                return null;
            }

            clrName = alias.Name;
        }

        return new ContractExport(name.Name, (int)arity.Value, determinism, arguments, clrName);
    }

    private static ContractArgument? ReadArgument(SyntaxTerm term, List<Diagnostic> diagnostics, string? fileName)
    {
        if (
            term is not CompoundTerm { Arity: 2 } specification
            || specification.Name is not ("in" or "out")
            || specification.Arguments[0] is not AtomTerm name
        )
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.InvalidArgument,
                "Expected in(Name, Type) or out(Name, Type).",
                term.Span,
                fileName
            );
            return null;
        }

        if (!SyntaxFacts.MapsToIdentifier(name.Name))
        {
            Report(
                diagnostics,
                CodeGenDiagnosticIds.NameDoesNotMapToIdentifier,
                $"'{name.Name}' does not map to a C# identifier; only letters, digits, and underscores do.",
                name.Span,
                fileName
            );

            return null;
        }

        ContractType? type = ReadType(specification.Arguments[1], diagnostics, fileName);
        if (type is null)
        {
            return null;
        }

        return new ContractArgument(specification.Name == "in" ? ArgumentMode.In : ArgumentMode.Out, name.Name, type);
    }

    private static ContractType? ReadType(SyntaxTerm term, List<Diagnostic> diagnostics, string? fileName)
    {
        switch (term)
        {
            case AtomTerm { Name: "atom" }:
                return new ContractType(ContractTypeKind.Atom);

            case AtomTerm { Name: "integer" }:
                return new ContractType(ContractTypeKind.Integer);

            case AtomTerm { Name: "float" }:
                return new ContractType(ContractTypeKind.Float);

            case AtomTerm { Name: "term" }:
                return new ContractType(ContractTypeKind.Term);

            case CompoundTerm { Name: "list", Arity: 1 } list:
            {
                ContractType? element = ReadType(list.Arguments[0], diagnostics, fileName);
                return element is null ? null : new ContractType(ContractTypeKind.List, element);
            }

            default:
                Report(
                    diagnostics,
                    CodeGenDiagnosticIds.UnknownType,
                    "Expected atom, integer, float, term, or list(Type).",
                    term.Span,
                    fileName
                );

                return null;
        }
    }

    private static bool TryReadDeterminism(string name, out Determinism determinism)
    {
        switch (name)
        {
            case "det":
                determinism = Determinism.Det;
                return true;
            case "semidet":
                determinism = Determinism.Semidet;
                return true;
            case "multi":
                determinism = Determinism.Multi;
                return true;
            case "nondet":
                determinism = Determinism.Nondet;
                return true;
            default:
                determinism = default;
                return false;
        }
    }

    private static void Report(List<Diagnostic> diagnostics, string id, string message, SourceSpan span, string? fileName) =>
        diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName));
}
