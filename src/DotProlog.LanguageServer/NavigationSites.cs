using DotProlog.Compiler;

namespace DotProlog.LanguageServer;

// A source location is usable only when all retained interpretations agree on its identity and role.
internal static class NavigationSites
{
    internal static PrologDefinitionSite? At(SourceOutline outline, int offset)
    {
        List<PrologDefinitionSite> matches = [];
        foreach (var context in Contexts(outline))
        {
            var sites = context
                .Where(site => offset >= site.Range.Start && offset < site.Range.Start + site.Range.Length)
                .ToArray();
            if (sites.Length == 0)
                return null;
            matches.AddRange(sites);
        }
        return Agree(matches);
    }

    internal static IEnumerable<PrologDefinitionSite> All(SourceOutline outline, CancellationToken cancellationToken)
    {
        var contexts = Contexts(outline).Select(context => context.ToLookup(site => site.Range)).ToArray();
        var ranges = contexts.SelectMany(context => context.Select(group => group.Key)).Distinct();
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contexts.Any(context => !context.Contains(range)))
                continue;
            if (Agree(contexts.SelectMany(context => context[range]).ToArray()) is { } site)
                yield return site;
        }
    }

    internal static bool SamePredicate(PrologDefinitionSite first, PrologDefinitionSite second) =>
        first.Module == second.Module
        && first.Name == second.Name
        && first.Arity == second.Arity
        && first.Definitions.Any(definition => second.Definitions.Any(other => SameDefinition(definition, other)));

    private static bool SameDefinition(PrologSourceSymbol first, PrologSourceSymbol second) =>
        DocumentSnapshot.KeyComparer.Equals(first.FileName, second.FileName)
        && first.Module == second.Module
        && first.Name == second.Name
        && first.Arity + (first.IsNonterminal ? 2 : 0) == second.Arity + (second.IsNonterminal ? 2 : 0)
        && first.SelectionRange == second.SelectionRange;

    private static PrologDefinitionSite? Agree(IReadOnlyList<PrologDefinitionSite> sites)
    {
        if (sites.Count == 0)
            return null;
        var first = sites[0];
        if (
            first.Module is null
            || sites.Any(site =>
                site.Module != first.Module
                || site.Name != first.Name
                || site.Arity != first.Arity
                || site.Range != first.Range
                || site.IsDefinition != first.IsDefinition
                || site.IsDeclaration != first.IsDeclaration
            )
        )
            return null;
        if (
            !first.Definitions.Any(definition =>
                sites.All(site => site.Definitions.Any(other => SameDefinition(definition, other)))
            )
        )
            return null;
        return first with { Definitions = sites.SelectMany(site => site.Definitions).Distinct().ToArray() };
    }

    private static IEnumerable<IReadOnlyList<PrologDefinitionSite>> Contexts(SourceOutline outline)
    {
        foreach (var analysis in outline.Definitions)
        {
            if (analysis.Count == 0)
                yield return Array.Empty<PrologDefinitionSite>();
            else
                foreach (var context in analysis.GroupBy(site => site.Context))
                    yield return context.ToArray();
        }
    }
}
