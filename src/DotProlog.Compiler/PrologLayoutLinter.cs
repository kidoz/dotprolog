using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Source-text half of <see cref="PrologLinter"/>.</summary>
internal static class PrologLayoutLinter
{
    internal static void Analyze(
        string source,
        IReadOnlyList<SyntaxTerm> clauses,
        PrologLintOptions options,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        Validate(options);
        var lines = new SourceLines(source);

        // Every rule that reads raw characters shares one classification of the source, so none of
        // them can disagree with the others about what is code, quoted text, or a comment.
        SourceRegion[] regions = ClassifyRegions(source);

        AnalyzeLines(source, lines, regions, options, fileName, diagnostics);
        if (options.RequireSpaceAfterComma)
        {
            AnalyzeCommas(source, lines, regions, fileName, diagnostics);
        }

        AnalyzeClauses(source, clauses, lines, regions, options, fileName, diagnostics);
    }

    private static void AnalyzeLines(
        string source,
        SourceLines lines,
        SourceRegion[] regions,
        PrologLintOptions options,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        foreach (SourceLine line in lines.All)
        {
            if (options.MaxLineLength is int maximum && line.Length > maximum)
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.LineTooLong,
                        $"Line has {line.Length} characters; the configured maximum is {maximum}.",
                        lines.Span(line.Start + maximum, line.Length - maximum),
                        fileName
                    )
                );
            }

            if (options.CheckTrailingWhitespace)
            {
                int trailingStart = line.End;
                while (trailingStart > line.Start && source[trailingStart - 1] is ' ' or '\t')
                {
                    trailingStart--;
                }

                if (trailingStart < line.End)
                {
                    diagnostics.Add(
                        Warning(
                            LintDiagnosticIds.TrailingWhitespace,
                            "Line ends with trailing whitespace.",
                            lines.Span(trailingStart, line.End - trailingStart),
                            fileName
                        )
                    );
                }
            }
        }

        if (!options.DisallowTabs)
        {
            return;
        }

        for (int offset = 0; offset < source.Length; offset++)
        {
            // A tab inside quoted text is the atom's own value, so no edit to the layout can remove
            // it. One in a comment is still layout a reader sees, and stays reportable.
            if (source[offset] == '\t' && regions[offset] != SourceRegion.Quoted)
            {
                diagnostics.Add(
                    Warning(LintDiagnosticIds.TabCharacter, "Use spaces instead of tabs.", lines.Span(offset, 1), fileName)
                );
            }
        }
    }

    private static void AnalyzeCommas(
        string source,
        SourceLines lines,
        SourceRegion[] regions,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        for (int offset = 0; offset < source.Length; offset++)
        {
            if (regions[offset] != SourceRegion.Code || source[offset] != ',')
            {
                continue;
            }

            char next = offset + 1 < source.Length ? source[offset + 1] : '\0';
            if (!char.IsWhiteSpace(next))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.MissingSpaceAfterComma,
                        "Follow a comma with whitespace.",
                        lines.Span(offset, 1),
                        fileName
                    )
                );
            }
        }
    }

    private static void AnalyzeClauses(
        string source,
        IReadOnlyList<SyntaxTerm> clauses,
        SourceLines lines,
        SourceRegion[] regions,
        PrologLintOptions options,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        int previousEndLine = 0;
        foreach (SyntaxTerm clause in clauses)
        {
            int startLine = clause.Span.Line;
            int endLine = lines.LineNumberAt(LastOffset(clause.Span));

            if (options.RequireClauseLayout && (clause.Span.Column != 1 || startLine == previousEndLine))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.ClauseLayout,
                        "Begin each clause on a new line at column one.",
                        clause.Span with
                        {
                            Length = Math.Min(1, clause.Span.Length),
                        },
                        fileName
                    )
                );
            }

            if (options.MaxClauseLines is int maximum && endLine - startLine + 1 > maximum)
            {
                SourceLine firstExcess = lines[startLine + maximum];
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.ClauseTooLong,
                        $"Clause spans more than the configured maximum of {maximum} lines.",
                        lines.Span(firstExcess.Start, Math.Min(1, firstExcess.Length)),
                        fileName
                    )
                );
            }

            if (options.IndentSize is int indentSize)
            {
                AnalyzeIndentation(source, lines, regions, startLine, endLine, indentSize, fileName, diagnostics);
            }

            if (
                options.RequireClauseLayout
                && clause is CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule
                && rule.Arguments[1].Span.Line <= lines.LineNumberAt(LastOffset(rule.Arguments[0].Span))
            )
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.ClauseLayout,
                        "Start a rule body on the line after its head.",
                        rule.Arguments[1].Span with
                        {
                            Length = Math.Min(1, rule.Arguments[1].Span.Length),
                        },
                        fileName
                    )
                );
            }

            if (options.RequireOneSubgoalPerLine)
            {
                AnalyzeSubgoals(clause, lines, fileName, diagnostics);
            }

            previousEndLine = endLine;
        }
    }

    private static void AnalyzeIndentation(
        string source,
        SourceLines lines,
        SourceRegion[] regions,
        int startLine,
        int endLine,
        int indentSize,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        for (int lineNumber = startLine + 1; lineNumber <= endLine; lineNumber++)
        {
            SourceLine line = lines[lineNumber];
            int content = line.Start;
            while (content < line.End && source[content] == ' ')
            {
                content++;
            }

            if (content == line.End)
            {
                continue;
            }

            // Only a continuation of the clause itself has indentation to get wrong. A line that
            // continues a multi-line quoted token carries that token's own text, where the leading
            // characters are its value; a comment is not a continuation at all.
            if (regions[content] != SourceRegion.Code)
            {
                continue;
            }

            int indentation = content - line.Start;
            if (indentation >= indentSize && indentation % indentSize == 0)
            {
                continue;
            }

            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.InconsistentIndentation,
                    $"Indent clause continuations by a positive multiple of {indentSize} spaces.",
                    lines.Span(content, Math.Min(1, line.End - content)),
                    fileName
                )
            );
        }
    }

    private static void AnalyzeSubgoals(SyntaxTerm clause, SourceLines lines, string? fileName, List<Diagnostic> diagnostics)
    {
        SyntaxTerm? goal = clause switch
        {
            CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule => rule.Arguments[1],
            CompoundTerm { Name: ":-", Arity: 1 } directive => directive.Arguments[0],
            _ => null,
        };
        if (goal is null)
        {
            return;
        }

        Stack<SyntaxTerm> pending = new();
        pending.Push(goal);

        while (pending.TryPop(out SyntaxTerm? term))
        {
            if (term is not CompoundTerm compound)
            {
                continue;
            }

            if (
                compound is { Name: ",", Arity: 2 }
                && compound.Arguments[1].Span.Line <= lines.LineNumberAt(LastOffset(compound.Arguments[0].Span))
            )
            {
                SyntaxTerm right = compound.Arguments[1];
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.SubgoalLayout,
                        "Start each conjunction subgoal on a separate line.",
                        right.Span with
                        {
                            Length = Math.Min(1, right.Span.Length),
                        },
                        fileName
                    )
                );
            }

            int controlArity = compound.Name switch
            {
                "," or ";" or "->" or "*->" when compound.Arity == 2 => 2,
                "\\+" or "once" or "ignore" when compound.Arity == 1 => 1,
                _ => 0,
            };
            for (int index = controlArity - 1; index >= 0; index--)
            {
                pending.Push(compound.Arguments[index]);
            }
        }
    }

    /// <summary>
    /// Classifies every character as code, quoted text, or a comment, in one pass that mirrors the
    /// reader's lexical rules. Rules that read raw characters consult this rather than each keeping
    /// their own idea of what is shielded.
    /// </summary>
    private static SourceRegion[] ClassifyRegions(ReadOnlySpan<char> source)
    {
        var regions = new SourceRegion[source.Length];

        for (int offset = 0; offset < source.Length; offset++)
        {
            char current = source[offset];
            char next = offset + 1 < source.Length ? source[offset + 1] : '\0';

            // Each shielded token decides only where it ends; one fill and one advance serve them
            // all, so no branch can leave the scan pointing at a character it already consumed.
            (int end, SourceRegion region) = (current, next) switch
            {
                ('%', _) => (LineCommentEnd(source, offset), SourceRegion.Comment),
                ('/', '*') => (BlockCommentEnd(source, offset), SourceRegion.Comment),
                ('\'', _) when IsCharacterCodeQuote(source, offset) => (CharacterCodeEnd(source, offset), SourceRegion.Quoted),
                ('\'' or '"' or '`', _) => (QuotedTokenEnd(source, offset), SourceRegion.Quoted),
                _ => (-1, SourceRegion.Code),
            };

            if (end < offset)
            {
                continue;
            }

            Array.Fill(regions, region, offset, end - offset + 1);
            offset = end;
        }

        return regions;
    }

    /// <summary>The last character of a line comment, excluding the newline that ends it, which stays
    /// code so the line-length and trailing-whitespace rules still see it.</summary>
    private static int LineCommentEnd(ReadOnlySpan<char> source, int start)
    {
        int newline = source[start..].IndexOf('\n');
        return newline < 0 ? source.Length - 1 : start + newline - 1;
    }

    /// <summary>The last character of a block comment. These do not nest, and an unterminated one
    /// runs to the end of the source.</summary>
    private static int BlockCommentEnd(ReadOnlySpan<char> source, int start)
    {
        int close = source[(start + 2)..].IndexOf("*/", StringComparison.Ordinal);
        return close < 0 ? source.Length - 1 : start + close + 3;
    }

    /// <summary>The closing delimiter of the quoted token opened at <paramref name="start"/>.</summary>
    private static int QuotedTokenEnd(ReadOnlySpan<char> source, int start)
    {
        char quote = source[start];
        for (int offset = start + 1; offset < source.Length; offset++)
        {
            // A backslash escapes the next character, including the newline of a line continuation.
            if (source[offset] == '\\')
            {
                offset++;
                continue;
            }

            if (source[offset] != quote)
            {
                continue;
            }

            // A doubled delimiter denotes the character itself and leaves the token open.
            if (offset + 1 < source.Length && source[offset + 1] == quote)
            {
                offset++;
                continue;
            }

            return offset;
        }

        // Unterminated: the reader reports it, and classification runs to the end of the source.
        return source.Length - 1;
    }

    /// <summary>
    /// Whether the quote at <paramref name="quoteOffset"/> belongs to a <c>0'c</c> character-code
    /// literal rather than opening a quoted token.
    /// </summary>
    /// <remarks>
    /// Only a standalone <c>0</c> introduces one, which is how <c>Lexer.ReadNumber</c> decides:
    /// <c>10'a'</c> and <c>x0'a'</c> are a number or a name followed by a quoted atom. There is no
    /// general <c>Base'Digits</c> radix form to consider, only <c>0x</c>, <c>0o</c>, and <c>0b</c>.
    /// </remarks>
    private static bool IsCharacterCodeQuote(ReadOnlySpan<char> source, int quoteOffset)
    {
        if (quoteOffset == 0 || source[quoteOffset - 1] != '0')
        {
            return false;
        }

        int preceding = quoteOffset - 2;
        return preceding < 0 || !(char.IsLetterOrDigit(source[preceding]) || source[preceding] is '_' or '.');
    }

    /// <summary>The offset of the last character of the character-code literal whose quote is at
    /// <paramref name="quoteOffset"/>, mirroring the escape and doubled-quote forms the lexer reads.</summary>
    private static int CharacterCodeEnd(ReadOnlySpan<char> source, int quoteOffset)
    {
        int body = quoteOffset + 1;
        if (body >= source.Length)
        {
            return quoteOffset;
        }

        // A numeric escape such as 0'\x41\ runs past this, but its remainder holds no quote, so
        // stopping at the escaped character cannot resynchronise the scan onto one.
        if (source[body] == '\\')
        {
            return Math.Min(body + 1, source.Length - 1);
        }

        return source[body] == '\'' && body + 1 < source.Length && source[body + 1] == '\'' ? body + 1 : body;
    }

    private static int LastOffset(SourceSpan span) => span.Start + Math.Max(0, span.Length - 1);

    private static void Validate(PrologLintOptions options)
    {
        ValidatePositive(options.IndentSize, nameof(options.IndentSize));
        ValidatePositive(options.MaxLineLength, nameof(options.MaxLineLength));
        ValidatePositive(options.MaxClauseLines, nameof(options.MaxClauseLines));
    }

    private static void ValidatePositive(int? value, string name)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Lint thresholds must be positive.");
        }
    }

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);

    /// <summary>Which lexical region a source character belongs to.</summary>
    private enum SourceRegion : byte
    {
        /// <summary>Ordinary program text, where layout rules apply.</summary>
        Code,

        /// <summary>A quoted token or a character-code literal, whose characters are its value.</summary>
        Quoted,

        /// <summary>A line or block comment, excluding the newline that ends a line comment.</summary>
        Comment,
    }

    private readonly record struct SourceLine(int Number, int Start, int Length)
    {
        internal int End => Start + Length;
    }

    private sealed class SourceLines
    {
        private readonly List<SourceLine> _lines = [];

        internal SourceLines(string source)
        {
            int start = 0;
            int number = 1;
            for (int offset = 0; offset < source.Length; offset++)
            {
                if (source[offset] != '\n')
                {
                    continue;
                }

                int length = offset - start;
                if (length > 0 && source[offset - 1] == '\r')
                {
                    length--;
                }

                _lines.Add(new SourceLine(number++, start, length));
                start = offset + 1;
            }

            _lines.Add(new SourceLine(number, start, source.Length - start));
        }

        internal IReadOnlyList<SourceLine> All => _lines;

        internal SourceLine this[int oneBasedLine] => _lines[oneBasedLine - 1];

        internal int LineNumberAt(int offset)
        {
            int lower = 0;
            int upper = _lines.Count - 1;
            while (lower <= upper)
            {
                int middle = lower + ((upper - lower) / 2);
                SourceLine line = _lines[middle];
                int nextStart = middle + 1 < _lines.Count ? _lines[middle + 1].Start : int.MaxValue;
                if (offset < line.Start)
                {
                    upper = middle - 1;
                }
                else if (offset >= nextStart)
                {
                    lower = middle + 1;
                }
                else
                {
                    return line.Number;
                }
            }

            return _lines[^1].Number;
        }

        internal SourceSpan Span(int offset, int length)
        {
            int lineNumber = LineNumberAt(offset);
            SourceLine line = this[lineNumber];
            return new SourceSpan(offset, length, lineNumber, offset - line.Start + 1);
        }
    }
}
