using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

internal static class DocumentDiagnostics
{
    internal static JsonArray Analyze(
        string text,
        string uri,
        PrologLanguageMode mode,
        CancellationToken cancellationToken = default
    ) => AnalyzeDocument(text, uri, mode, cancellationToken).Diagnostics;

    internal static (JsonArray Diagnostics, SourceOutline Outline) AnalyzeDocument(
        string text,
        string uri,
        PrologLanguageMode mode,
        CancellationToken cancellationToken
    )
    {
        var program = new BytecodeProgram(mode);
        ParseResult parsed = TermReader.ReadProgram(
            text,
            uri,
            program.Operators,
            program.CharacterConversions,
            program.Flags,
            clauseBoundary: _ => cancellationToken.ThrowIfCancellationRequested()
        );
        IReadOnlyList<Diagnostic> warnings = PrologLinter.AnalyzeSource(
            text,
            parsed.Clauses,
            uri,
            initialDoubleQuotes: program.InitialDoubleQuotes,
            codePoints: program.Flags.CodePointCharacters
        );
        cancellationToken.ThrowIfCancellationRequested();
        var source = new PrologLintSource(
            uri,
            text,
            parsed.Clauses,
            program.InitialDoubleQuotes,
            program.Flags.CodePointCharacters
        );
        return (
            Convert(text, parsed.Diagnostics.Concat(warnings)),
            new SourceOutline(
                text,
                PrologSourceSymbols.Analyze([source], cancellationToken),
                PrologBuiltinHovers.Analyze([source], mode, cancellationToken),
                PrologCompletionText.Create(text, cancellationToken),
                [PrologCompletionIndex.Create([source], mode, cancellationToken)],
                [PrologSourceDefinitions.Analyze([source], cancellationToken)]
            )
        );
    }

    internal static JsonArray Convert(string text, IEnumerable<Diagnostic> diagnostics)
    {
        var positions = new SourcePositions(text);
        JsonArray result = [];
        foreach (
            Diagnostic diagnostic in diagnostics.OrderBy(item => item.Span.Start).ThenBy(item => item.Id, StringComparer.Ordinal)
        )
        {
            result.Add(
                (JsonNode)
                    new JsonObject
                    {
                        ["range"] = positions.Range(diagnostic.Span),
                        ["severity"] = diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 2,
                        ["code"] = diagnostic.Id,
                        ["source"] = "DotProlog",
                        ["message"] = diagnostic.Message,
                    }
            );
        }
        return result;
    }
}
