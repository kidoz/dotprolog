using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Finds double-quoted text whose reading under the <c>double_quotes</c> flag disagrees with how the
/// program uses it: a literal handed to a conversion that needs the other list kind, and a literal
/// read as characters that is parsed by a grammar comparing character codes.
/// </summary>
/// <remarks>
/// The flag is tracked through the file the way the loader tracks it, without running anything: it
/// starts at the host's initial value, and a <c>:- set_prolog_flag(double_quotes, Value).</c>
/// directive governs the clauses after it. Grammar analysis sees only the rules of the same file.
/// </remarks>
internal static class DoubleQuotesLinter
{
    private static readonly HashSet<string> ArithmeticComparisons = ["<", ">", "=<", ">=", "=:=", "=\\="];

    private static readonly HashSet<string> GrammarControls = [",", ";", "|", "->", "*->"];

    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        DoubleQuotesMode initialDoubleQuotes,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        var readings = new DoubleQuotesMode[clauses.Count];
        Dictionary<(string Name, int Arity), List<GrammarRule>> grammar = [];
        List<(int Clause, GrammarRule Rule)> rules = [];

        DoubleQuotesMode current = initialDoubleQuotes;
        for (var index = 0; index < clauses.Count; index++)
        {
            SyntaxTerm clause = clauses[index];
            readings[index] = current;

            if (TryDoubleQuotesDirective(clause, out DoubleQuotesMode selected))
            {
                current = selected;
                continue;
            }

            if (
                clause is CompoundTerm { Name: "-->", Arity: 2 } rule
                && TryNonterminal(GrammarHead(rule.Arguments[0]), out var key)
            )
            {
                GrammarRule analyzed = GrammarRule.Create(rule.Arguments[1]);
                if (!grammar.TryGetValue(key, out List<GrammarRule>? definitions))
                {
                    definitions = [];
                    grammar.Add(key, definitions);
                }

                definitions.Add(analyzed);
                rules.Add((index, analyzed));
            }
        }

        for (var index = 0; index < clauses.Count; index++)
        {
            CheckConversions(clauses[index], readings[index], fileName, diagnostics);
            if (readings[index] == DoubleQuotesMode.Chars)
            {
                CheckPhraseCalls(clauses[index], grammar, fileName, diagnostics);
            }
        }

        foreach ((var clause, GrammarRule rule) in rules)
        {
            if (readings[clause] != DoubleQuotesMode.Chars || rule.TextTerminals.Count == 0)
            {
                continue;
            }

            if (FirstCodeTest([rule], rule.Calls, grammar) is { } evidence)
            {
                diagnostics.Add(GrammarWarning(rule.TextTerminals[0].Span, evidence, fileName));
            }
        }
    }

    /// <summary>Reports a non-empty literal passed where a conversion needs the other list kind.</summary>
    private static void CheckConversions(
        SyntaxTerm clause,
        DoubleQuotesMode reading,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        foreach (CompoundTerm goal in Compounds(clause))
        {
            if (goal is not { Arity: 2, Arguments: [_, StringTerm { Value.Length: > 0 } text] })
            {
                continue;
            }

            ListKind? expected = goal.Name switch
            {
                "atom_codes" or "number_codes" => ListKind.Codes,
                "atom_chars" or "number_chars" => ListKind.Chars,
                "string_codes" or "string_chars" => ListKind.Either,
                _ => null,
            };

            if (expected is not { } kind || Accepts(kind, reading))
            {
                continue;
            }

            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.DoubleQuotedListKindMismatch,
                    DiagnosticSeverity.Warning,
                    $"This double-quoted text reads as {Reading(reading)} (double_quotes={FlagValue(reading)}), "
                        + $"but {goal.Name}/2 expects {Expected(kind)}; {ConversionFix(goal.Name, kind, reading)}.",
                    text.Span,
                    fileName
                )
            );
        }
    }

    /// <summary>Reports text read as characters that <c>phrase/2,3</c> hands to a code-testing grammar.</summary>
    private static void CheckPhraseCalls(
        SyntaxTerm clause,
        Dictionary<(string Name, int Arity), List<GrammarRule>> grammar,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        foreach (CompoundTerm goal in Compounds(clause))
        {
            if (
                goal is not { Name: "phrase", Arity: 2 or 3 }
                || goal.Arguments[1] is not StringTerm { Value.Length: > 0 } text
                || !TryNonterminal(goal.Arguments[0], out var start)
            )
            {
                continue;
            }

            if (FirstCodeTest([], [start], grammar) is { } evidence)
            {
                diagnostics.Add(GrammarWarning(text.Span, evidence, fileName));
            }
        }
    }

    /// <summary>
    /// The earliest code test in <paramref name="rules"/> or in any rule reachable from
    /// <paramref name="calls"/>, or <see langword="null"/> when the reachable grammar has none.
    /// </summary>
    private static SourceSpan? FirstCodeTest(
        IEnumerable<GrammarRule> rules,
        IEnumerable<(string Name, int Arity)> calls,
        Dictionary<(string Name, int Arity), List<GrammarRule>> grammar
    )
    {
        List<SourceSpan> evidence = [.. rules.SelectMany(rule => rule.CodeTests)];
        HashSet<(string Name, int Arity)> visited = [];
        Queue<(string Name, int Arity)> pending = new(calls);

        while (pending.TryDequeue(out var nonterminal))
        {
            if (!visited.Add(nonterminal) || !grammar.TryGetValue(nonterminal, out List<GrammarRule>? definitions))
            {
                continue;
            }

            foreach (GrammarRule rule in definitions)
            {
                evidence.AddRange(rule.CodeTests);
                foreach (var call in rule.Calls)
                {
                    pending.Enqueue(call);
                }
            }
        }

        return evidence.Count == 0 ? null : evidence.MinBy(span => span.Start);
    }

    private static Diagnostic GrammarWarning(SourceSpan text, SourceSpan evidence, string? fileName) =>
        new(
            LintDiagnosticIds.CodeGrammarOverCharacters,
            DiagnosticSeverity.Warning,
            "This double-quoted text reads as a list of characters (double_quotes=chars), but the grammar that "
                + $"parses it compares character codes at line {evidence.Line}, column {evidence.Column}; rewrite "
                + "the grammar over characters or set double_quotes to codes for this file.",
            text,
            fileName
        );

    /// <summary>Every compound subterm of <paramref name="term"/>, walked without recursion.</summary>
    private static IEnumerable<CompoundTerm> Compounds(SyntaxTerm term)
    {
        Stack<SyntaxTerm> pending = new();
        pending.Push(term);

        while (pending.TryPop(out SyntaxTerm? current))
        {
            if (current is not CompoundTerm compound)
            {
                continue;
            }

            yield return compound;
            for (var index = compound.Arguments.Count - 1; index >= 0; index--)
            {
                pending.Push(compound.Arguments[index]);
            }
        }
    }

    /// <summary>Recognizes exactly the directive shape the loader applies while reading a file.</summary>
    private static bool TryDoubleQuotesDirective(SyntaxTerm clause, out DoubleQuotesMode selected)
    {
        selected = default;
        if (
            clause
            is not CompoundTerm
            {
                Name: ":-",
                Arity: 1,
                Arguments: [
                    CompoundTerm
                    {
                        Name: "set_prolog_flag",
                        Arity: 2,
                        Arguments: [AtomTerm { Name: "double_quotes" }, AtomTerm value],
                    },
                ],
            }
        )
        {
            return false;
        }

        (var known, selected) = value.Name switch
        {
            "codes" => (true, DoubleQuotesMode.Codes),
            "chars" => (true, DoubleQuotesMode.Chars),
            "atom" => (true, DoubleQuotesMode.Atom),
            "string" => (true, DoubleQuotesMode.String),
            _ => (false, default(DoubleQuotesMode)),
        };

        return known;
    }

    /// <summary>The nonterminal of a grammar rule head, without its pushback list.</summary>
    private static SyntaxTerm GrammarHead(SyntaxTerm head) =>
        head is CompoundTerm { Name: ",", Arity: 2 } pushback ? pushback.Arguments[0] : head;

    /// <summary>Names a nonterminal as <c>Name//Arity</c>, looking through module qualification.</summary>
    private static bool TryNonterminal(SyntaxTerm term, out (string Name, int Arity) key)
    {
        while (term is CompoundTerm { Name: ":", Arity: 2 } qualified)
        {
            term = qualified.Arguments[1];
        }

        (var found, key) = term switch
        {
            AtomTerm atom => (true, (atom.Name, 0)),
            CompoundTerm compound => (true, (compound.Name, compound.Arity)),
            _ => (false, default((string, int))),
        };

        return found;
    }

    private static bool Accepts(ListKind expected, DoubleQuotesMode reading) =>
        expected switch
        {
            ListKind.Codes => reading == DoubleQuotesMode.Codes,
            ListKind.Chars => reading == DoubleQuotesMode.Chars,
            _ => reading is DoubleQuotesMode.Codes or DoubleQuotesMode.Chars,
        };

    private static string Reading(DoubleQuotesMode reading) =>
        reading switch
        {
            DoubleQuotesMode.Codes => "a list of character codes",
            DoubleQuotesMode.Chars => "a list of characters",
            DoubleQuotesMode.Atom => "an atom",
            _ => "a string",
        };

    private static string Expected(ListKind kind) =>
        kind switch
        {
            ListKind.Codes => "a list of character codes",
            ListKind.Chars => "a list of characters",
            _ => "a list of characters or codes",
        };

    private static string FlagValue(DoubleQuotesMode reading) =>
        reading switch
        {
            DoubleQuotesMode.Codes => "codes",
            DoubleQuotesMode.Chars => "chars",
            DoubleQuotesMode.Atom => "atom",
            _ => "string",
        };

    private static string ConversionFix(string predicate, ListKind kind, DoubleQuotesMode reading) =>
        (kind, reading) switch
        {
            (ListKind.Codes, DoubleQuotesMode.Chars) =>
                $"use {predicate.Replace("_codes", "_chars", StringComparison.Ordinal)}/2 or set double_quotes to codes for this file",
            (ListKind.Chars, DoubleQuotesMode.Codes) =>
                $"use {predicate.Replace("_chars", "_codes", StringComparison.Ordinal)}/2 or set double_quotes to chars for this file",
            (ListKind.Codes, _) => "set double_quotes to codes for this file",
            _ => "set double_quotes to chars for this file",
        };

    private enum ListKind
    {
        Codes,
        Chars,
        Either,
    }

    /// <summary>What one grammar rule body calls, which text it matches, and where it tests codes.</summary>
    private sealed record GrammarRule(
        List<(string Name, int Arity)> Calls,
        List<StringTerm> TextTerminals,
        List<SourceSpan> CodeTests
    )
    {
        /// <summary>
        /// Walks a rule body through the grammar controls. A code test is an integer inside a list
        /// terminal, or — inside <c>{}/1</c> — an arithmetic comparison, <c>between/3</c>, or
        /// <c>code_type/2</c> that treats a variable matched by a list terminal as a code.
        /// </summary>
        internal static GrammarRule Create(SyntaxTerm body)
        {
            List<(string Name, int Arity)> calls = [];
            List<StringTerm> text = [];
            List<SourceSpan> codeTests = [];
            List<SyntaxTerm> goals = [];
            HashSet<string> matched = new(StringComparer.Ordinal);

            Stack<SyntaxTerm> pending = new();
            pending.Push(body);
            while (pending.TryPop(out SyntaxTerm? term))
            {
                switch (term)
                {
                    case CompoundTerm control when control.Arity == 2 && GrammarControls.Contains(control.Name):
                        pending.Push(control.Arguments[1]);
                        pending.Push(control.Arguments[0]);
                        break;
                    case CompoundTerm { Name: "\\+", Arity: 1 } negation:
                        pending.Push(negation.Arguments[0]);
                        break;
                    case CompoundTerm { Name: ":", Arity: 2 } qualified:
                        pending.Push(qualified.Arguments[1]);
                        break;
                    case CompoundTerm { Name: "{}", Arity: 1 } goal:
                        goals.Add(goal.Arguments[0]);
                        break;
                    case CompoundTerm { Name: TermReader.ListFunctor, Arity: 2 } terminal:
                        ReadTerminal(terminal, matched, codeTests);
                        break;
                    case StringTerm { Value.Length: > 0 } literal:
                        text.Add(literal);
                        break;
                    case CompoundTerm { Name: "call" }:
                    case AtomTerm { Name: TermReader.EmptyListAtom or "!" }:
                        break;
                    case AtomTerm atom:
                        calls.Add((atom.Name, 0));
                        break;
                    case CompoundTerm nonterminal:
                        calls.Add((nonterminal.Name, nonterminal.Arity));
                        break;
                    default:
                        break;
                }
            }

            foreach (SyntaxTerm goal in goals)
            {
                foreach (CompoundTerm test in Compounds(goal))
                {
                    if (IsCodeTest(test, matched))
                    {
                        codeTests.Add(test.Span);
                    }
                }
            }

            return new GrammarRule(calls, text, codeTests);
        }

        private static void ReadTerminal(CompoundTerm terminal, HashSet<string> matched, List<SourceSpan> codeTests)
        {
            SyntaxTerm rest = terminal;
            while (rest is CompoundTerm { Name: TermReader.ListFunctor, Arity: 2 } cell)
            {
                switch (cell.Arguments[0])
                {
                    case IntegerTerm code:
                        codeTests.Add(code.Span);
                        break;
                    case VariableTerm { IsAnonymous: false } element:
                        matched.Add(element.Name);
                        break;
                    default:
                        break;
                }

                rest = cell.Arguments[1];
            }
        }

        private static bool IsCodeTest(CompoundTerm test, HashSet<string> matched) =>
            test switch
            {
                { Arity: 2, Arguments: [VariableTerm left, IntegerTerm] } when ArithmeticComparisons.Contains(test.Name) =>
                    matched.Contains(left.Name),
                { Arity: 2, Arguments: [IntegerTerm, VariableTerm right] } when ArithmeticComparisons.Contains(test.Name) =>
                    matched.Contains(right.Name),
                { Name: "between", Arity: 3, Arguments: [IntegerTerm, IntegerTerm, VariableTerm value] } => matched.Contains(
                    value.Name
                ),
                { Name: "code_type", Arity: 2, Arguments: [VariableTerm code, _] } => matched.Contains(code.Name),
                _ => false,
            };
    }
}
