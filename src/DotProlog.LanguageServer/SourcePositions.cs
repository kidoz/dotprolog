using System.Text.Json.Nodes;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

internal sealed class SourcePositions
{
    private readonly string _text;
    private readonly List<int> _lines = [0];

    internal SourcePositions(string text)
    {
        _text = text;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                _lines.Add(index + 1);
            }
            else if (text[index] == '\n')
                _lines.Add(index + 1);
        }
    }

    internal JsonObject Range(SourceSpan span) =>
        new() { ["start"] = Position(span.Start), ["end"] = Position(span.Start + span.Length) };

    internal int? Offset(int line, int character)
    {
        if (line < 0 || character < 0 || line >= _lines.Count)
            return null;
        int start = _lines[line];
        int end = line + 1 < _lines.Count ? _lines[line + 1] : _text.Length;
        while (end > start && _text[end - 1] is '\r' or '\n')
            end--;
        return start + Math.Min(character, end - start);
    }

    private JsonObject Position(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        var line = _lines.BinarySearch(offset);
        if (line < 0)
            line = ~line - 1;
        return new JsonObject { ["line"] = line, ["character"] = offset - _lines[line] };
    }
}
