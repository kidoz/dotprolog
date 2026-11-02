using System.Text.Json.Nodes;
using DotProlog.Compiler;

namespace DotProlog.LanguageServer;

// Constructed in the analysis worker and never mutated after publication.
internal sealed class ReferenceIndex
{
    private readonly Dictionary<(string Module, string Name, int Arity), List<Entry>> _references = [];

    internal static ReferenceIndex Create(
        IReadOnlyDictionary<string, SourceOutline> documents,
        CancellationToken cancellationToken
    )
    {
        var result = new ReferenceIndex();
        foreach (var (file, outline) in documents.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string uri = new Uri(file, UriKind.Absolute).AbsoluteUri;
            var positions = new SourcePositions(outline.Text);
            foreach (var site in NavigationSites.All(outline, cancellationToken).OrderBy(site => site.Range.Start))
            {
                var key = (site.Module!, site.Name, site.Arity);
                if (!result._references.TryGetValue(key, out var entries))
                {
                    entries = [];
                    result._references.Add(key, entries);
                }
                entries.Add(new Entry(site, uri, positions));
            }
        }
        return result;
    }

    internal JsonArray Find(SourceOutline? outline, int line, int character, bool includeDeclaration)
    {
        JsonArray locations = [];
        if (
            outline is null
            || new SourcePositions(outline.Text).Offset(line, character) is not { } offset
            || NavigationSites.At(outline, offset) is not { } origin
            || !_references.TryGetValue((origin.Module!, origin.Name, origin.Arity), out var entries)
        )
            return locations;
        foreach (var entry in entries)
        {
            if ((!includeDeclaration && entry.Site.IsDefinition) || !NavigationSites.SamePredicate(origin, entry.Site))
                continue;
            locations.Add((JsonNode)new JsonObject { ["uri"] = entry.Uri, ["range"] = entry.Positions.Range(entry.Site.Range) });
        }
        return locations;
    }

    private sealed record Entry(PrologDefinitionSite Site, string Uri, SourcePositions Positions);
}
