using System.Globalization;
using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

internal static class DocumentCompletion
{
    internal static JsonArray Convert(SourceOutline? outline, string file, int line, int character)
    {
        JsonArray items = [];
        if (outline is null)
            return items;
        var positions = new SourcePositions(outline.Text);
        if (positions.Offset(line, character) is not { } offset || outline.CompletionText.At(offset) is not { } prefix)
            return items;
        if (prefix.IsVariable || prefix.Text.Length == 0 && prefix.Module is null)
            foreach (string variable in prefix.Variables.Where(name => name.StartsWith(prefix.Text, StringComparison.Ordinal)))
                items.Add((JsonNode)Item(variable, variable, 6, "Clause variable", null, positions, prefix.Range));
        if (prefix.IsVariable || outline.Completions.Count == 0)
            return items;

        // A shared include may be interpreted in several module contexts. Never select one arbitrarily.
        IEnumerable<PrologPredicateCompletion> candidates = outline.Completions[0].At(file, offset, prefix.Module);
        foreach (var context in outline.Completions.Skip(1))
            candidates = candidates.Intersect(context.At(file, offset, prefix.Module));
        foreach (var candidate in candidates.Where(candidate => candidate.Name.StartsWith(prefix.Text, StringComparison.Ordinal)))
            items.Add(
                (JsonNode)Item(
                    candidate.Name + "/" + candidate.Arity.ToString(CultureInfo.InvariantCulture),
                    candidate.Name,
                    3,
                    candidate.Module is null ? "Builtin predicate" : "Predicate in " + candidate.Module,
                    candidate.Documentation?.Description,
                    positions,
                    prefix.Range
                )
            );
        return items;
    }

    private static JsonObject Item(
        string label,
        string name,
        int kind,
        string detail,
        string? documentation,
        SourcePositions positions,
        SourceSpan range
    )
    {
        var item = new JsonObject
        {
            ["label"] = label,
            ["kind"] = kind,
            ["detail"] = detail,
            ["filterText"] = name,
            ["insertTextFormat"] = 1,
            ["textEdit"] = new JsonObject { ["range"] = positions.Range(range), ["newText"] = name },
        };
        if (documentation is not null)
            item["documentation"] = documentation;
        return item;
    }
}
