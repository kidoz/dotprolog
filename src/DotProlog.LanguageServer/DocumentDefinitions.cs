using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal static class DocumentDefinitions
{
    internal static JsonArray Convert(
        SourceOutline? outline,
        IReadOnlyDictionary<string, SourceOutline> documents,
        int line,
        int character,
        bool links
    )
    {
        JsonArray result = [];
        if (outline is null || new SourcePositions(outline.Text).Offset(line, character) is not { } offset)
            return result;
        if (NavigationSites.At(outline, offset) is not { } site)
            return result;
        foreach (
            var target in site
                .Definitions.Distinct()
                .OrderBy(target => target.FileName, StringComparer.Ordinal)
                .ThenBy(target => target.SelectionRange.Start)
        )
        {
            if (!documents.TryGetValue(target.FileName, out var document))
                continue;
            string uri = new Uri(target.FileName, UriKind.Absolute).AbsoluteUri;
            var positions = new SourcePositions(document.Text);
            result.Add(
                (JsonNode)(
                    links
                        ? new JsonObject
                        {
                            ["originSelectionRange"] = new SourcePositions(outline.Text).Range(site.Range),
                            ["targetUri"] = uri,
                            ["targetRange"] = positions.Range(target.Range),
                            ["targetSelectionRange"] = positions.Range(target.SelectionRange),
                        }
                        : new JsonObject { ["uri"] = uri, ["range"] = positions.Range(target.SelectionRange) }
                )
            );
        }
        return result;
    }
}
