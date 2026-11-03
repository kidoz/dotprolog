using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A conservative, non-executing predicate-rename view of one complete source snapshot.</summary>
public sealed class PrologRenameAnalysis
{
    private static readonly OperatorTable Operators = new();

    private PrologRenameAnalysis(
        IReadOnlyList<PrologDefinitionSite> sites,
        IReadOnlyList<(string FileName, int Context)> contexts,
        string? blockingReason
    )
    {
        Sites = sites;
        Contexts = contexts;
        BlockingReason = blockingReason;
    }

    /// <summary>Heads, calls and declaration name tokens, with physical expansion identities.</summary>
    public IReadOnlyList<PrologDefinitionSite> Sites { get; }

    /// <summary>Physical expansions, including those with no editable predicate occurrences.</summary>
    public IReadOnlyList<(string FileName, int Context)> Contexts { get; }

    /// <summary>Why this snapshot cannot prove a complete rename; null when eligible.</summary>
    public string? BlockingReason { get; }

    /// <summary>Accepts plain ASCII atom names, excluding operators, builtins and compiler hooks.</summary>
    public static bool IsAllowedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0
            && name[0] is >= 'a' and <= 'z'
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            && !Operators.IsOperator(name)
            && !LintBuiltinCatalog.ContainsName(name)
            && name is not ("term_expansion" or "goal_expansion");
    }

    /// <summary>Analyzes supplied sources only; callers must report missing or unparsed source content.</summary>
    public static PrologRenameAnalysis Create(
        IReadOnlyList<PrologLintSource> sources,
        bool hasAnalysisGaps = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        cancellationToken.ThrowIfCancellationRequested();
        var model = PrologLintProgramModel.Build(sources);
        List<PrologDefinitionSite> sites = [.. PrologSourceDefinitions.Analyze(model, cancellationToken)];
        var definitions = sites
            .Where(site => site.IsDefinition)
            .GroupBy(site => (site.Module, site.Name, site.Arity))
            .ToDictionary(group => group.Key, group => group.First().Definitions);
        string? reason = null;
        void Block(string message) => reason ??= message;
        if (hasAnalysisGaps || sources.Any(source => source.HasAnalysisGaps))
            Block("Source analysis is incomplete; repair parse errors or missing sources first.");
        if (model.HasUnresolvedImports)
            Block("Imports must resolve without conflicts before rename.");
        if (HasImportCycle(model))
            Block("Cyclic imports require additional rename analysis.");
        if (sites.Any(site => site.IsDefinition && !IsAllowedName(site.Name)))
            Block("Operator, builtin or hook definitions require additional rename analysis.");

        foreach (var call in PrologCallSites.Analyze(model, PrologLanguageMode.Modern, cancellationToken, Block))
        {
            if (call.DefinitionModule is null && (call.Documentation is null || !SafeBuiltin(call.Predicate.Name)))
                Block($"Call to {call.Predicate} is unresolved or may inspect or change predicate identities.");
        }
        foreach (var source in sources)
        {
            string module = ModuleTable.UserModule;
            for (int index = 0; index < source.Clauses.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clause = source.Clauses[index];
                if (clause is not CompoundTerm { Name: ":-", Arity: 1 } directiveClause)
                {
                    if (!LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause), false, out _))
                        Block("A clause has no statically identifiable head.");
                    continue;
                }
                if (directiveClause.Arguments[0] is not CompoundTerm directive)
                {
                    Block("This directive requires additional rename analysis.");
                    continue;
                }
                SyntaxTerm? indicators = null;
                bool template = false;
                switch (directive)
                {
                    case { Name: "module", Arity: 1 or 2, Arguments: [AtomTerm name, ..] }:
                        module = name.Name;
                        if (directive.Arity == 2)
                            indicators = directive.Arguments[1];
                        break;
                    case { Name: "body", Arity: 1, Arguments: [AtomTerm name] }:
                        module = name.Name;
                        break;
                    case { Name: "end_body" or "end_module", Arity: 1 }:
                        module = ModuleTable.UserModule;
                        break;
                    case { Name: "export" or "multifile" or "discontiguous", Arity: 1 }:
                        indicators = directive.Arguments[0];
                        break;
                    case { Name: "meta_predicate", Arity: 1 }:
                        indicators = directive.Arguments[0];
                        template = true;
                        break;
                    case { Name: "use_module" or "import" or "reexport", Arity: 1 or 2 }:
                        if (directive.Arity == 2)
                            indicators = directive.Arguments[1];
                        break;
                    case { Name: "initialization", Arity: 1 or 2 }:
                        break;
                    default:
                        Block($"Directive {directive.Name}/{directive.Arity} requires additional rename analysis.");
                        break;
                }
                if (indicators is null)
                    continue;
                foreach (var item in LintSyntax.SequenceItems(indicators))
                {
                    PredicateIndicator predicate;
                    SourceSpan range;
                    if (template && LintSyntax.TryPredicate(item, false, out predicate))
                    {
                        range = item is CompoundTerm { NameSpan: { } nameSpan } ? nameSpan : item.Span;
                    }
                    else if (
                        !template
                        && LintSyntax.TryIndicator(item, out predicate, out _)
                        && item is CompoundTerm { Arguments: [AtomTerm atom, _] }
                    )
                    {
                        range = atom.Span;
                    }
                    else
                    {
                        Block("A declaration has no statically identifiable predicate name.");
                        continue;
                    }
                    var owner = model.Resolve(module, predicate).Module;
                    var targets = definitions.GetValueOrDefault((owner, predicate.Name, predicate.Arity)) ?? [];
                    if (targets.Count == 0)
                        Block("A declared predicate has no source implementation.");
                    sites.Add(
                        new PrologDefinitionSite(
                            source.FileForClause(index),
                            range,
                            owner,
                            predicate.Name,
                            predicate.Arity,
                            targets,
                            model.ContextOf(clause)
                        )
                        {
                            IsDeclaration = true,
                        }
                    );
                }
            }
        }
        return new PrologRenameAnalysis(
            sites,
            model.ModuleScopes.Select(scope => (scope.File, scope.Context)).Distinct().ToArray(),
            reason
        );
    }

    // Deliberately an allowlist. Reflection, dynamic loading/database operations and reader changes
    // require data-flow analysis before their atom arguments can be safely excluded from edits.
    private static bool SafeBuiltin(string name) =>
        name
            is ","
                or ";"
                or "|"
                or "->"
                or "*->"
                or "\\+"
                or "!"
                or "true"
                or "false"
                or "fail"
                or "repeat"
                or "call"
                or "phrase"
                or "once"
                or "ignore"
                or "catch"
                or "throw"
                or "findall"
                or "bagof"
                or "setof"
                or "forall"
                or "maplist"
                or "include"
                or "exclude"
                or "partition"
                or "foldl"
                or "="
                or "\\="
                or "=="
                or "\\=="
                or "@<"
                or "@>"
                or "@=<"
                or "@>="
                or "compare"
                or "is"
                or "=:="
                or "=\\="
                or "<"
                or ">"
                or "=<"
                or ">="
                or "var"
                or "nonvar"
                or "atom"
                or "integer"
                or "float"
                or "number"
                or "atomic"
                or "compound"
                or "ground"
                or "acyclic_term"
                or "copy_term"
                or "unify_with_occurs_check"
                or "arg"
                or "member"
                or "memberchk"
                or "append"
                or "length"
                or "reverse"
                or "sort"
                or "msort"
                or "keysort"
                or "select"
                or "nth0"
                or "nth1"
                or "last"
                or "sum_list"
                or "max_list"
                or "min_list"
                or "between"
                or "succ"
                or "write"
                or "writeq"
                or "write_canonical"
                or "writeln"
                or "nl";

    private static bool HasImportCycle(PrologLintProgramModel model)
    {
        var pending = model
            .Imports.GroupBy(import => import.Importer)
            .ToDictionary(group => group.Key, group => group.Select(import => import.SourceModule).ToArray());
        while (pending.Count > 0)
        {
            string[] ready =
            [
                .. pending
                    .Where(group => group.Value.All(dependency => dependency is null || !pending.ContainsKey(dependency)))
                    .Select(group => group.Key),
            ];
            if (ready.Length == 0)
                return true;
            foreach (string module in ready)
                pending.Remove(module);
        }
        return false;
    }
}
