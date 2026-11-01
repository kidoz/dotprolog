using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Validates literal calls, arithmetic expressions, and goals with predetermined outcomes.</summary>
internal static class PrologLiteralLinter
{
    private static readonly HashSet<string> ArithmeticComparisons = ["=:=", "=\\=", "<", ">", "=<", ">="];
    private static readonly HashSet<string> ControlNames =
    [
        ",",
        ";",
        "|",
        "->",
        "*->",
        ":-",
        "-->",
        "{}",
        ":",
        "initialization",
        "set_prolog_flag",
        "op",
        "dynamic",
        "discontiguous",
        "multifile",
        "meta_predicate",
        "module",
        "use_module",
        "ensure_loaded",
        "include",
        "export",
        "import",
        "reexport",
        "end_module",
        "body",
        "end_body",
        "char_conversion",
    ];

    internal static void Analyze(IReadOnlyList<SyntaxTerm> clauses, string? fileName, List<Diagnostic> diagnostics)
    {
        HashSet<PredicateIndicator> definitions = Definitions(clauses);
        foreach (SyntaxTerm clause in clauses)
        {
            foreach (SyntaxTerm body in LintGoalWalker.Bodies(clause))
            {
                foreach (CompoundTerm goal in LintGoalWalker.Goals(body))
                {
                    CheckArity(goal, definitions, fileName, diagnostics);
                    CheckFormat(goal, fileName, diagnostics);
                    CheckOptions(goal, fileName, diagnostics);
                    CheckArithmetic(goal, fileName, diagnostics);
                    CheckPredetermined(goal, clause, fileName, diagnostics);
                }
            }
        }
    }

    private static HashSet<PredicateIndicator> Definitions(IReadOnlyList<SyntaxTerm> clauses)
    {
        HashSet<PredicateIndicator> result = [];
        foreach (SyntaxTerm clause in clauses)
        {
            var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
            if (LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause), grammar, out PredicateIndicator indicator))
            {
                result.Add(indicator);
            }
        }

        return result;
    }

    private static void CheckArity(
        CompoundTerm goal,
        HashSet<PredicateIndicator> definitions,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        if (ControlNames.Contains(goal.Name))
        {
            return;
        }

        PredicateIndicator indicator = new(goal.Name, goal.Arity);
        if (
            LintBuiltinCatalog.Contains(indicator)
            || definitions.Contains(indicator)
            || !LintBuiltinCatalog.ContainsName(goal.Name)
        )
        {
            return;
        }

        diagnostics.Add(
            Warning(
                LintDiagnosticIds.PossibleBuiltinArityMismatch,
                $"No built-in or file-local predicate {goal.Name}/{goal.Arity} exists; {goal.Name} is built in with a different arity. "
                    + "If this is a user predicate defined elsewhere, no action is needed.",
                goal.Span,
                fileName
            )
        );
    }

    private static void CheckFormat(CompoundTerm goal, string? fileName, List<Diagnostic> diagnostics)
    {
        if (goal.Name != "format" || goal.Arity is < 1 or > 3)
        {
            return;
        }

        SyntaxTerm format = goal.Arguments[goal.Arity == 3 ? 1 : 0];
        SyntaxTerm arguments = goal.Arity == 1 ? new AtomTerm("[]", goal.Span) : goal.Arguments[goal.Arity - 1];
        if (!TryText(format, out var text))
        {
            return;
        }

        if (!TryParseFormat(text, out List<FormatDirective> directives, out var error))
        {
            diagnostics.Add(Warning(LintDiagnosticIds.InvalidFormatCall, error!, format.Span, fileName));
            return;
        }

        if (!LintSyntax.TryProperList(arguments, out List<SyntaxTerm> values))
        {
            if (arguments is not VariableTerm and not StringTerm)
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.InvalidFormatCall,
                        "The format argument list is not a proper list; evaluation would raise an error.",
                        arguments.Span,
                        fileName
                    )
                );
            }

            return;
        }

        var required = directives.Sum(directive => directive.ConsumedArguments);
        if (required != values.Count)
        {
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.InvalidFormatCall,
                    $"Format directives consume {required} argument(s), but the literal list contains {values.Count}.",
                    arguments.Span,
                    fileName
                )
            );
            return;
        }

        var index = 0;
        foreach (FormatDirective directive in directives)
        {
            if (
                directive.Star
                && values[index] is not (VariableTerm or StringTerm)
                && !AcceptsNumber(values[index], integerOnly: true)
            )
            {
                FormatTypeWarning(values[index], "an integer repeat count", fileName, diagnostics);
            }

            if (directive.Star)
            {
                index++;
            }

            switch (directive.Name)
            {
                case 'a' when values[index] is not (VariableTerm or StringTerm) && !AcceptsAtomText(values[index]):
                    FormatTypeWarning(values[index], "an atom or string", fileName, diagnostics);
                    break;
                case 'd'
                or 'D'
                or 'c' when values[index] is not (VariableTerm or StringTerm) && !AcceptsNumber(values[index], integerOnly: true):
                    FormatTypeWarning(values[index], "an integer", fileName, diagnostics);
                    break;
                case 'e'
                or 'f'
                or 'g'
                or 'r'
                or 'R'
                    when values[index] is not (VariableTerm or StringTerm) && !AcceptsNumber(values[index], integerOnly: false):
                    FormatTypeWarning(values[index], "a number", fileName, diagnostics);
                    break;
                case 'W':
                    ValidateOptionList(values[index + 1], OptionListKind.Write, fileName, diagnostics);
                    break;
            }

            index += directive.ConsumedArguments - (directive.Star ? 1 : 0);
        }
    }

    private static bool TryParseFormat(string text, out List<FormatDirective> directives, out string? error)
    {
        directives = [];
        error = null;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '~')
            {
                continue;
            }

            index++;
            if (index >= text.Length)
            {
                error = "The format string ends after '~' without a directive.";
                return false;
            }

            var star = false;
            if (text[index] == '`')
            {
                index++;
                if (index >= text.Length)
                {
                    error = "The format string ends before the fill character and directive after '~`'.";
                    return false;
                }

                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    index++;
                }

                index++;
            }
            else if (text[index] == '*')
            {
                star = true;
                index++;
            }
            else
            {
                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }

            if (index >= text.Length)
            {
                error = "The format string ends before its directive character.";
                return false;
            }

            char name = text[index];
            var consumed = name switch
            {
                'w' or 'p' or 'q' or 'a' or 'd' or 'D' or 'e' or 'f' or 'g' or 'r' or 'R' or 's' or 'c' or 'i' or '@' => 1,
                'W' => 2,
                'n' or 't' or '|' or '+' or '~' => 0,
                _ => -1,
            };
            if (consumed < 0)
            {
                error = $"Unknown format directive '~{name}'.";
                return false;
            }

            directives.Add(new FormatDirective(name, star, consumed + (star ? 1 : 0)));
        }

        return true;
    }

    private static void CheckOptions(CompoundTerm goal, string? fileName, List<Diagnostic> diagnostics)
    {
        OptionListKind? kind = goal switch
        {
            { Name: "open", Arity: 4 } => OptionListKind.Open,
            { Name: "close", Arity: 2 } => OptionListKind.Close,
            { Name: "read_term", Arity: 2 or 3 } => OptionListKind.Read,
            { Name: "write_term", Arity: 2 or 3 } => OptionListKind.Write,
            _ => null,
        };
        if (kind is { } selected)
        {
            ValidateOptionList(goal.Arguments[^1], selected, fileName, diagnostics);
        }
    }

    private static void ValidateOptionList(SyntaxTerm term, OptionListKind kind, string? fileName, List<Diagnostic> diagnostics)
    {
        if (!LintSyntax.TryProperList(term, out List<SyntaxTerm> options))
        {
            if (term is not VariableTerm and not StringTerm)
            {
                OptionWarning(term, kind, "is not a proper option list", fileName, diagnostics);
            }

            return;
        }

        foreach (SyntaxTerm option in options)
        {
            if (option is not CompoundTerm { Arity: 1 } compound)
            {
                OptionWarning(option, kind, "must be a unary option term", fileName, diagnostics);
                continue;
            }

            SyntaxTerm value = compound.Arguments[0];
            if (value is StringTerm)
            {
                continue;
            }

            var valid = (kind, compound.Name) switch
            {
                (OptionListKind.Open, "alias") => value is AtomTerm,
                (OptionListKind.Open, "type") => value is AtomTerm { Name: "text" or "binary" },
                (OptionListKind.Open, "reposition") => IsBoolean(value),
                (OptionListKind.Open, "eof_action") => value is AtomTerm { Name: "error" or "eof_code" or "reset" },
                (OptionListKind.Close, "force") => IsBoolean(value),
                (OptionListKind.Read, "variable_names" or "variables" or "singletons") => true,
                (OptionListKind.Write, "quoted" or "ignore_ops" or "numbervars") => IsBoolean(value),
                (OptionListKind.Write, "spacing") => value is AtomTerm { Name: "standard" or "next_argument" },
                (OptionListKind.Write, "variable_names") => ValidVariableNames(value),
                _ => false,
            };
            if (!valid)
            {
                OptionWarning(option, kind, "is unknown or has an invalid value", fileName, diagnostics);
            }
        }
    }

    private static bool ValidVariableNames(SyntaxTerm value)
    {
        if (!LintSyntax.TryProperList(value, out List<SyntaxTerm> names))
        {
            return false;
        }

        return names.All(item => item is CompoundTerm { Name: "=", Arity: 2, Arguments: [AtomTerm, _] });
    }

    private static void CheckArithmetic(CompoundTerm goal, string? fileName, List<Diagnostic> diagnostics)
    {
        if (goal is { Name: "is", Arity: 2 })
        {
            CheckArithmeticExpression(goal.Arguments[1], fileName, diagnostics);
        }
        else if (goal.Arity == 2 && ArithmeticComparisons.Contains(goal.Name))
        {
            CheckArithmeticExpression(goal.Arguments[0], fileName, diagnostics);
            CheckArithmeticExpression(goal.Arguments[1], fileName, diagnostics);
        }
    }

    private static void CheckArithmeticExpression(SyntaxTerm term, string? fileName, List<Diagnostic> diagnostics)
    {
        switch (term)
        {
            case VariableTerm or IntegerTerm or BigIntegerTerm or FloatTerm or RationalTerm:
                return;
            case AtomTerm atom when ArithmeticEvaluator.IsKnownEvaluable(atom.Name, 0):
                return;
            case CompoundTerm compound when ArithmeticEvaluator.IsKnownEvaluable(compound.Name, compound.Arity):
                if (compound.Name is "/" or "rdiv" or "//" or "div" or "mod" or "rem" && IsZero(compound.Arguments[1]))
                {
                    ArithmeticWarning(compound.Arguments[1], "has a zero divisor", fileName, diagnostics);
                    return;
                }

                foreach (SyntaxTerm argument in compound.Arguments)
                {
                    CheckArithmeticExpression(argument, fileName, diagnostics);
                }

                return;
            default:
                ArithmeticWarning(term, "is not an evaluable arithmetic expression", fileName, diagnostics);
                return;
        }
    }

    private static void CheckPredetermined(CompoundTerm goal, SyntaxTerm clause, string? fileName, List<Diagnostic> diagnostics)
    {
        if (goal.Arity == 2 && goal.Name is "=" or "unify_with_occurs_check" or "\\=" or "==" or "\\==")
        {
            SyntaxTerm left = goal.Arguments[0];
            SyntaxTerm right = goal.Arguments[1];
            bool? equal = GroundEquality(left, right);
            if (equal is null && left is VariableTerm leftVariable && right is VariableTerm rightVariable)
            {
                if (leftVariable.Name == rightVariable.Name && !leftVariable.IsAnonymous)
                {
                    equal = true;
                }
                else if (goal.Name is "==" or "\\==" && IsFresh(leftVariable, clause) && IsFresh(rightVariable, clause))
                {
                    equal = false;
                }
            }

            if (equal is not { } same)
            {
                return;
            }

            var positive = goal.Name is "=" or "unify_with_occurs_check" or "==";
            var goalSucceeds = positive == same;
            if (!goalSucceeds)
            {
                diagnostics.Add(
                    Warning(LintDiagnosticIds.AlwaysFalseGoal, $"Goal {goal.Name}/2 always fails here.", goal.Span, fileName)
                );
            }
            else if (
                ReferenceEquals(left, right)
                || left is VariableTerm && right is VariableTerm
                || IsGround(left) && IsGround(right)
            )
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.NoEffectGoal,
                        $"Goal {goal.Name}/2 always succeeds here without a useful effect.",
                        goal.Span,
                        fileName
                    )
                );
            }

            return;
        }

        if (
            goal.Arity != 1
            || goal.Name
                is not (
                    "var"
                    or "nonvar"
                    or "atom"
                    or "integer"
                    or "float"
                    or "number"
                    or "atomic"
                    or "compound"
                    or "callable"
                    or "ground"
                    or "acyclic_term"
                )
        )
        {
            return;
        }

        SyntaxTerm argument = goal.Arguments[0];
        bool? succeeds = goal.Name switch
        {
            "var" when IsGround(argument) => false,
            "nonvar" when IsGround(argument) => true,
            "atom" => argument is AtomTerm ? true
            : IsGround(argument) ? false
            : null,
            "integer" => argument is IntegerTerm or BigIntegerTerm ? true
            : IsGround(argument) ? false
            : null,
            "float" => argument is FloatTerm ? true
            : IsGround(argument) ? false
            : null,
            "number" => argument is IntegerTerm or BigIntegerTerm or FloatTerm or RationalTerm ? true
            : IsGround(argument) ? false
            : null,
            "atomic" => argument is AtomTerm or IntegerTerm or BigIntegerTerm or FloatTerm or RationalTerm or StringValueTerm
                ? true
            : IsGround(argument) ? false
            : null,
            "compound" => argument is CompoundTerm ? true
            : IsGround(argument) ? false
            : null,
            "callable" => argument is AtomTerm or CompoundTerm ? true
            : IsGround(argument) ? false
            : null,
            "ground" or "acyclic_term" when IsGround(argument) => true,
            _ => null,
        };
        if (succeeds is null)
        {
            return;
        }

        diagnostics.Add(
            succeeds.Value
                ? Warning(LintDiagnosticIds.NoEffectGoal, $"Type test {goal.Name}/1 always succeeds here.", goal.Span, fileName)
                : Warning(LintDiagnosticIds.AlwaysFalseGoal, $"Type test {goal.Name}/1 always fails here.", goal.Span, fileName)
        );
    }

    private static bool? GroundEquality(SyntaxTerm left, SyntaxTerm right)
    {
        if (!IsGround(left) || !IsGround(right))
        {
            return null;
        }

        return (left, right) switch
        {
            (AtomTerm a, AtomTerm b) => a.Name == b.Name,
            (IntegerTerm a, IntegerTerm b) => a.Value == b.Value,
            (BigIntegerTerm a, BigIntegerTerm b) => a.Value == b.Value,
            (FloatTerm a, FloatTerm b) => a.Value.Equals(b.Value),
            (RationalTerm a, RationalTerm b) => a.Numerator == b.Numerator && a.Denominator == b.Denominator,
            (StringValueTerm a, StringValueTerm b) => a.Value == b.Value,
            (CompoundTerm a, CompoundTerm b) when a.Name == b.Name && a.Arity == b.Arity => a
                .Arguments.Zip(b.Arguments)
                .All(pair => GroundEquality(pair.First, pair.Second) == true),
            _ => false,
        };
    }

    private static bool IsGround(SyntaxTerm term) =>
        term switch
        {
            VariableTerm or StringTerm => false,
            CompoundTerm compound => compound.Arguments.All(IsGround),
            _ => true,
        };

    private static bool IsFresh(VariableTerm variable, SyntaxTerm clause) =>
        variable.IsAnonymous || LintSyntax.Variables(clause).Count(item => item.Name == variable.Name) == 1;

    private static bool TryText(SyntaxTerm term, out string text)
    {
        switch (term)
        {
            case AtomTerm atom:
                text = atom.Name;
                return true;
            case StringTerm value:
                text = value.Value;
                return true;
            case StringValueTerm value:
                text = value.Value;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool AcceptsNumber(SyntaxTerm term, bool integerOnly) =>
        term is IntegerTerm or BigIntegerTerm || !integerOnly && term is FloatTerm or RationalTerm;

    private static bool AcceptsAtomText(SyntaxTerm term) => term is AtomTerm or StringValueTerm;

    private static bool IsZero(SyntaxTerm term) =>
        term switch
        {
            IntegerTerm integer => integer.Value == 0,
            BigIntegerTerm integer => integer.Value.IsZero,
            FloatTerm number => number.Value == 0,
            RationalTerm rational => rational.Numerator.IsZero,
            _ => false,
        };

    private static bool IsBoolean(SyntaxTerm term) => term is AtomTerm { Name: "true" or "false" };

    private static void FormatTypeWarning(SyntaxTerm term, string expected, string? fileName, List<Diagnostic> diagnostics) =>
        diagnostics.Add(
            Warning(LintDiagnosticIds.InvalidFormatCall, $"This format argument must be {expected}.", term.Span, fileName)
        );

    private static void ArithmeticWarning(SyntaxTerm term, string problem, string? fileName, List<Diagnostic> diagnostics) =>
        diagnostics.Add(
            Warning(LintDiagnosticIds.InvalidArithmeticExpression, $"Arithmetic expression {problem}.", term.Span, fileName)
        );

    private static void OptionWarning(
        SyntaxTerm term,
        OptionListKind kind,
        string problem,
        string? fileName,
        List<Diagnostic> diagnostics
    ) =>
        diagnostics.Add(
            Warning(
                LintDiagnosticIds.InvalidPredicateOption,
                $"The {kind.ToString().ToLowerInvariant()} option {problem}.",
                term.Span,
                fileName
            )
        );

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);

    private readonly record struct FormatDirective(char Name, bool Star, int ConsumedArguments);

    private enum OptionListKind
    {
        Open,
        Close,
        Read,
        Write,
    }
}
