using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

// Shared, non-executing traversal of goal positions. Data terms never become calls by name alone.
internal static class PrologCallSites
{
    internal static IReadOnlyList<PrologCallSite> Analyze(
        PrologLintProgramModel model,
        PrologLanguageMode mode,
        CancellationToken cancellationToken
    )
    {
        List<PrologCallSite> result = [];
        foreach (var clause in model.Clauses)
        {
            Stack<Goal> pending = new();
            switch (clause.Clause)
            {
                case CompoundTerm { Name: ":-", Arity: 2 } rule:
                    pending.Push(new Goal(rule.Arguments[1], clause.Module));
                    break;
                case CompoundTerm { Name: "-->", Arity: 2 } grammar:
                    pending.Push(new Goal(grammar.Arguments[1], clause.Module, Grammar: true));
                    break;
                case CompoundTerm
                {
                    Name: ":-",
                    Arity: 1,
                    Arguments: [CompoundTerm { Name: "initialization", Arity: 1 or 2 } init]
                }:
                    pending.Push(new Goal(init.Arguments[0], clause.Module));
                    break;
            }
            while (pending.TryPop(out var site))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var term = site.Term;
                if (term is CompoundTerm { Name: ":", Arity: 2 } qualified)
                {
                    pending.Push(
                        site with
                        {
                            Term = qualified.Arguments[1],
                            Module = site.Module is null ? null : (qualified.Arguments[0] as AtomTerm)?.Name,
                        }
                    );
                    continue;
                }
                if (site.Existential && term is CompoundTerm { Name: "^", Arity: 2 } existential)
                {
                    pending.Push(site with { Term = existential.Arguments[1] });
                    continue;
                }
                if (
                    site.Extra == 0
                    && term is CompoundTerm control
                    && (
                        (control.Arity == 2 && control.Name is "," or ";" or "|" or "->" or "*->")
                        || (control.Arity == 1 && control.Name == "\\+")
                    )
                )
                {
                    Add(control, site.Module, 0, grammar: site.HideHover, intrinsic: true);
                    for (int index = control.Arity - 1; index >= 0; index--)
                        pending.Push(site with { Term = control.Arguments[index] });
                    continue;
                }
                if (site.Grammar)
                {
                    if (term is CompoundTerm { Name: "{}", Arity: 1 } ordinary)
                        pending.Push(site with { Term = ordinary.Arguments[0], Grammar = false });
                    else if (term is AtomTerm { Name: "!" })
                        Add(term, site.Module, 0, grammar: site.HideHover, intrinsic: true);
                    else if (term is CompoundTerm { Name: "call" or "phrase", Arity: 1 } metaGrammar)
                    {
                        if (Add(term, site.Module, 2, grammar: true) is not null)
                            pending.Push(
                                new Goal(
                                    metaGrammar.Arguments[0],
                                    site.Module,
                                    Extra: metaGrammar.Name == "call" ? 2 : 0,
                                    Grammar: metaGrammar.Name == "phrase",
                                    HideHover: true
                                )
                            );
                    }
                    else if (term is not AtomTerm { Name: "[]" or "{}" } && term is not CompoundTerm { Name: ".", Arity: 2 })
                        Add(term, site.Module, 2, grammar: true);
                    // DCG sites are retained for navigation but excluded from ordinary builtin hover.
                    continue;
                }
                var resolution = Add(term, site.Module, site.Extra, grammar: site.HideHover);
                if (site.Extra != 0 || term is not CompoundTerm goal || site.Module is null)
                    continue;
                var indicator = new PredicateIndicator(goal.Name, goal.Arity);
                if (goal.Name == "phrase" && goal.Arity is 2 or 3 && resolution is not null)
                {
                    pending.Push(new Goal(goal.Arguments[0], site.Module, Grammar: true, HideHover: site.HideHover));
                    continue;
                }
                // Do not apply a predefined meta template to an overriding ordinary user predicate.
                var resolved = model.Resolve(site.Module, indicator);
                bool declaredMeta =
                    resolved.Module is { } owner
                    && model.Modules.Catalog.TryGet(owner, out var definition)
                    && definition!.TryPredicate(new ModulePredicateIndicator(goal.Name, goal.Arity), out var metadata)
                    && metadata!.MetapredicateTemplate is not null;
                if (resolution is null && !declaredMeta)
                    continue;
                int[]? meta = model.MetaArgumentsOf(site.Module, indicator);
                if (meta is null)
                    continue;
                for (var index = 0; index < Math.Min(meta.Length, goal.Arity); index++)
                    if (meta[index] >= 0)
                        pending.Push(
                            new Goal(
                                goal.Arguments[index],
                                site.Module,
                                meta[index],
                                Existential: resolution is not null && goal.Name is "bagof" or "setof" && index == 1,
                                HideHover: site.HideHover
                            )
                        );
            }

            PrologBuiltinDocumentation? Add(
                SyntaxTerm term,
                string? module,
                int extra,
                bool grammar = false,
                bool intrinsic = false
            )
            {
                string name;
                int arity;
                SourceSpan range;
                switch (term)
                {
                    case AtomTerm atom:
                        name = atom.Name;
                        arity = extra;
                        range = atom.Span;
                        break;
                    case CompoundTerm { NameSpan: { } nameSpan } compound:
                        name = compound.Name;
                        arity = compound.Arity + extra;
                        range = nameSpan;
                        break;
                    default:
                        return null;
                }
                var predicate = new PredicateIndicator(name, arity);
                PrologBuiltinDocumentation? help = null;
                string? owner = null;
                if (
                    !intrinsic
                    && !(arity == 0 && name is "!" or "true" or "fail" or "false")
                    && module is not null
                    && model.Resolve(module, predicate) is { IsDefined: true } source
                    && (!model.HasUnresolvedImport(module, predicate) || model.Modules.Defines(module, predicate))
                )
                    owner = source.Module;
                if (module is not null && model.Modules.Catalog.Contains(module) && !model.HasUnresolvedImport(module, predicate))
                {
                    var resolution = model.Resolve(module, predicate);
                    if (
                        resolution.IsBuiltin
                        || (!resolution.IsDefined && !resolution.IsUnknown && BuiltinHelpCatalog.IsControl(predicate))
                    )
                        help = BuiltinHelpCatalog.Find(predicate, mode);
                }
                result.Add(
                    new PrologCallSite(
                        clause.Source.FileName,
                        range,
                        predicate,
                        owner,
                        grammar,
                        help,
                        model.ContextOf(clause.Clause)
                    )
                );
                return help;
            }
        }
        return result;
    }

    private sealed record Goal(
        SyntaxTerm Term,
        string? Module,
        int Extra = 0,
        bool Grammar = false,
        bool Existential = false,
        bool HideHover = false
    );
}
