using DotProlog.Syntax;

namespace DotProlog.Compiler;

internal sealed class CompilationSourceMap
{
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

    // Clauses from different included files can have identical spans and contents. Keep their
    // origins by identity, and pass that origin to replacement heads during lowering.
    private readonly Dictionary<SyntaxTerm, string> _documents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, CompiledSourceLocation> _locations = [];

    internal IReadOnlyDictionary<int, CompiledSourceLocation> Locations => _locations;

    internal void Register(string? fileName, string text)
    {
        if (fileName is not null)
        {
            _sources[fileName] = text;
        }
    }

    internal void SetDocument(SyntaxTerm term, string? fileName)
    {
        if (fileName is not null)
        {
            _documents[term] = fileName;
        }
    }

    internal string? DocumentOf(SyntaxTerm term, string? fallback) =>
        _documents.TryGetValue(term, out var document) ? document : fallback;

    internal void Record(int address, string? fileName, SourceSpan span)
    {
        if (fileName is null || span.Length <= 0 || !_sources.TryGetValue(fileName, out var text))
        {
            return;
        }

        // Invalid ranges must not become zero-width sequence points: the Portable PDB format
        // reserves a zero line/column delta for hidden points, which have a different encoding.
        if (span.Start < 0 || span.Start > text.Length || span.Length > text.Length - span.Start)
        {
            return;
        }

        var start = span.Start;
        var end = start + span.Length;
        var line = span.Line;
        var column = span.Column;
        for (var index = start; index < end; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        _locations.TryAdd(address, new CompiledSourceLocation(fileName, span.Line, span.Column, line, column));
    }
}
