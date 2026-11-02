using System.Globalization;
using System.Text.Json.Nodes;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

internal static class DocumentSymbols
{
    internal static JsonArray Convert(string uri, SourceOutline? outline, bool hierarchical)
    {
        JsonArray result = [];
        if (outline is null)
            return result;
        var positions = new SourcePositions(outline.Text);
        foreach (
            var group in outline
                .Symbols.Distinct()
                .OrderBy(symbol => symbol.Range.Start)
                .GroupBy(symbol => (symbol.Module, symbol.Name, symbol.Arity, symbol.IsNonterminal))
        )
        {
            var first = group.First();
            var separator = first.IsNonterminal ? "//" : "/";
            string indicator = first.Name + separator + first.Arity.ToString(CultureInfo.InvariantCulture);
            string name = first.Module == "user" ? indicator : first.Module + ":" + indicator;
            if (!hierarchical)
            {
                result.Add(
                    (JsonNode)
                        new JsonObject
                        {
                            ["name"] = name,
                            ["kind"] = 12,
                            ["containerName"] = first.Module,
                            ["location"] = new JsonObject { ["uri"] = uri, ["range"] = positions.Range(first.SelectionRange) },
                        }
                );
                continue;
            }
            SourceSpan range = group.Aggregate(first.Range, (span, symbol) => span.To(symbol.Range));
            JsonObject parent = Symbol(name, range, first.SelectionRange);
            parent["detail"] = first.IsNonterminal ? "DCG" : "predicate";
            if (group.Count() > 1)
            {
                JsonArray children = [];
                var index = 0;
                foreach (var clause in group)
                    children.Add(
                        (JsonNode)Symbol(
                            "clause " + (++index).ToString(CultureInfo.InvariantCulture),
                            clause.Range,
                            clause.SelectionRange
                        )
                    );
                parent["children"] = children;
            }
            result.Add((JsonNode)parent);
        }
        return result;

        JsonObject Symbol(string name, SourceSpan range, SourceSpan selection) =>
            new()
            {
                ["name"] = name,
                ["kind"] = 12,
                ["range"] = positions.Range(range),
                ["selectionRange"] = positions.Range(selection),
            };
    }
}
