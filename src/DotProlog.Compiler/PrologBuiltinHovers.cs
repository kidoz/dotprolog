using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Finds builtin help at statically identifiable goal positions without executing source.</summary>
public static class PrologBuiltinHovers
{
    /// <summary>Analyzes supplied sources with their module/include contexts and the selected language mode.</summary>
    public static IReadOnlyList<PrologBuiltinHover> Analyze(
        IReadOnlyList<PrologLintSource> sources,
        PrologLanguageMode mode,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = PrologLintProgramModel.Build(sources);
        List<PrologBuiltinHover> result = [];
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
                            Module = (qualified.Arguments[0] as AtomTerm)?.Name,
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
                    Add(control, site.Module, 0);
                    for (int index = control.Arity - 1; index >= 0; index--)
                        pending.Push(site with { Term = control.Arguments[index] });
                    continue;
                }
                if (site.Grammar)
                {
                    if (term is CompoundTerm { Name: "{}", Arity: 1 } ordinary)
                        pending.Push(site with { Term = ordinary.Arguments[0], Grammar = false });
                    else if (term is AtomTerm { Name: "!" })
                        Add(term, site.Module, 0);
                    // Grammar calls gain state arguments; they are not ordinary builtin calls.
                    continue;
                }
                var resolution = Add(term, site.Module, site.Extra);
                if (site.Extra != 0 || term is not CompoundTerm goal || site.Module is null)
                    continue;
                var indicator = new PredicateIndicator(goal.Name, goal.Arity);
                if (goal.Name == "phrase" && goal.Arity is 2 or 3 && resolution is not null)
                {
                    pending.Push(new Goal(goal.Arguments[0], site.Module, Grammar: true));
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
                                Existential: resolution is not null && goal.Name is "bagof" or "setof" && index == 1
                            )
                        );
            }

            PrologBuiltinDocumentation? Add(SyntaxTerm term, string? module, int extra)
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
                if (module is not null && model.Modules.Catalog.Contains(module) && !model.HasUnresolvedImport(module, predicate))
                {
                    var resolution = model.Resolve(module, predicate);
                    if (
                        resolution.IsBuiltin
                        || (!resolution.IsDefined && !resolution.IsUnknown && BuiltinHelpCatalog.IsControl(predicate))
                    )
                        help = BuiltinHelpCatalog.Find(predicate, mode);
                }
                result.Add(new PrologBuiltinHover(clause.Source.FileName, range, help));
                return help;
            }
        }
        return result;
    }

    private sealed record Goal(SyntaxTerm Term, string? Module, int Extra = 0, bool Grammar = false, bool Existential = false);
}
