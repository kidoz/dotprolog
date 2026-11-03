using System.Text.Json.Nodes;
using DotProlog.Compiler;

namespace DotProlog.LanguageServer;

internal static class DocumentRename
{
    internal static JsonObject? Prepare(IReadOnlyDictionary<string, SourceOutline> outlines, string uri, int line, int character)
    {
        try
        {
            var selection = Select(outlines, uri, line, character);
            return new JsonObject
            {
                ["range"] = new SourcePositions(outlines[DocumentSnapshot.Key(uri)].Text).Range(selection.Origin.Range),
                ["placeholder"] = selection.Origin.Name,
            };
        }
        catch (AnalysisRequestException)
        {
            return null;
        }
    }

    internal static JsonObject Rename(
        IReadOnlyDictionary<string, SourceOutline> outlines,
        IReadOnlyDictionary<string, DocumentSnapshot> documents,
        string uri,
        int line,
        int character,
        string newName
    )
    {
        if (!PrologRenameAnalysis.IsAllowedName(newName))
            throw new AnalysisRequestException(
                -32602,
                "Use a plain ASCII atom name beginning with a lowercase letter; builtin, operator and hook names are reserved."
            );
        var selection = Select(outlines, uri, line, character);
        JsonArray changes = [];
        if (selection.Origin.Name == newName)
            return new JsonObject { ["documentChanges"] = changes };
        if (
            selection
                .Analyses.SelectMany(analysis => analysis.Sites)
                .Any(site => site.Name == newName && site.Arity == selection.Origin.Arity)
        )
            throw new AnalysisRequestException(
                -32803,
                "The new name collides with another predicate of the same callable arity."
            );
        foreach (
            var group in selection
                .Sites.GroupBy(site => site.FileName, DocumentSnapshot.KeyComparer)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            var positions = new SourcePositions(outlines[group.Key].Text);
            JsonArray edits = [];
            foreach (var range in group.Select(site => site.Range).Distinct().OrderBy(range => range.Start))
                edits.Add((JsonNode)new JsonObject { ["range"] = positions.Range(range), ["newText"] = newName });
            documents.TryGetValue(group.Key, out var document);
            changes.Add(
                (JsonNode)
                    new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = document?.Uri ?? new Uri(group.Key, UriKind.Absolute).AbsoluteUri,
                            ["version"] = document?.Version,
                        },
                        ["edits"] = edits,
                    }
            );
        }
        return new JsonObject { ["documentChanges"] = changes };
    }

    private static Selection Select(IReadOnlyDictionary<string, SourceOutline> outlines, string uri, int line, int character)
    {
        string key = DocumentSnapshot.Key(uri);
        if (
            !outlines.TryGetValue(key, out var outline)
            || new SourcePositions(outline.Text).Offset(line, character) is not { } offset
            || NavigationSites.At(View(outline, key), offset) is not { } origin
            || !PrologRenameAnalysis.IsAllowedName(origin.Name)
        )
            throw new AnalysisRequestException(-32803, "No unambiguous source predicate can be renamed at this position.");
        var analyses = outlines
            .Values.SelectMany(value => value.Renames)
            .Distinct()
            .Where(analysis => analysis.Sites.Any(site => NavigationSites.SamePredicate(site, origin)))
            .ToHashSet();
        var sites = analyses
            .SelectMany(analysis => analysis.Sites)
            .Where(site => NavigationSites.SamePredicate(site, origin))
            .ToArray();
        foreach (var group in sites.GroupBy(site => site.FileName, DocumentSnapshot.KeyComparer))
        {
            if (!outlines.TryGetValue(group.Key, out var target))
                throw new AnalysisRequestException(-32803, "A rename target is unavailable in this snapshot.");
            // Every interpretation of a physical token must agree, including expansions where it is data.
            analyses.UnionWith(target.Renames);
            var view = View(target, group.Key);
            int end = -1;
            foreach (var site in group.DistinctBy(site => site.Range).OrderBy(site => site.Range.Start))
            {
                var agreed = NavigationSites.At(view, site.Range.Start);
                if (
                    agreed is null
                    || !NavigationSites.SamePredicate(agreed, origin)
                    || agreed.Range != site.Range
                    || site.Range.Start < end
                    || site.Range.Length <= 0
                    || site.Range.Start + site.Range.Length > target.Text.Length
                )
                    throw new AnalysisRequestException(
                        -32803,
                        "A shared source token has ambiguous or overlapping interpretations."
                    );
                end = site.Range.Start + site.Range.Length;
            }
        }
        foreach (var analysis in analyses)
            if (analysis.BlockingReason is { } reason)
                throw new AnalysisRequestException(-32803, reason);
        return new Selection(origin, sites, analyses.ToArray());
    }

    private static SourceOutline View(SourceOutline outline, string key) =>
        outline with
        {
            Definitions = outline
                .Renames.SelectMany(analysis =>
                {
                    var contexts = analysis
                        .Contexts.Where(context => DocumentSnapshot.KeyComparer.Equals(context.FileName, key))
                        .ToArray();
                    return contexts.Length == 0
                        ? new IReadOnlyList<PrologDefinitionSite>[] { [] }
                        : contexts.Select(context =>
                            (IReadOnlyList<PrologDefinitionSite>)
                                analysis
                                    .Sites.Where(site =>
                                        DocumentSnapshot.KeyComparer.Equals(site.FileName, key) && site.Context == context.Context
                                    )
                                    .ToArray()
                        );
                })
                .ToArray(),
        };

    private sealed record Selection(
        PrologDefinitionSite Origin,
        IReadOnlyList<PrologDefinitionSite> Sites,
        IReadOnlyList<PrologRenameAnalysis> Analyses
    );
}
