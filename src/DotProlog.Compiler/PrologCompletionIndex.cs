using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>A cached, non-executing view of visible predicate completions and physical source module scopes.</summary>
public sealed class PrologCompletionIndex
{
    private readonly Dictionary<string, PrologPredicateCompletion[]> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Offset, string Module)[][]> _scopes = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    private PrologCompletionIndex() { }

    /// <summary>Builds candidate sets using the same resolution model as source diagnostics.</summary>
    public static PrologCompletionIndex Create(
        IReadOnlyList<PrologLintSource> sources,
        PrologLanguageMode mode,
        CancellationToken cancellationToken = default
    )
    {
        var model = PrologLintProgramModel.Build(sources);
        var result = new PrologCompletionIndex();
        PredicateIndicator[] signatures =
        [
            .. LintBuiltinCatalog
                .All.Concat([new PredicateIndicator("call", 1), new PredicateIndicator("catch", 3)])
                .Concat(
                    model
                        .Modules.Catalog.Definitions.SelectMany(module => module.Predicates)
                        .Select(predicate => new PredicateIndicator(predicate.Indicator.Name, predicate.Indicator.Arity))
                )
                .Distinct()
                .Where(predicate => IsWord(predicate.Name))
                .OrderBy(predicate => predicate.Name, StringComparer.Ordinal)
                .ThenBy(predicate => predicate.Arity),
        ];
        foreach (var module in model.Modules.Catalog.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<PrologPredicateCompletion> candidates = [];
            foreach (PredicateIndicator signature in signatures)
            {
                var resolution = model.Resolve(module.Name, signature);
                if (resolution.IsDefined)
                    candidates.Add(new(signature.Name, signature.Arity, resolution.Module, null));
                else if (
                    (resolution.IsBuiltin || BuiltinHelpCatalog.IsControl(signature))
                    && !model.HasUnresolvedImport(module.Name, signature)
                )
                {
                    var help = BuiltinHelpCatalog.Find(signature, mode);
                    if (help is { Availability: null })
                        candidates.Add(new(signature.Name, signature.Arity, null, help));
                }
            }
            result._candidates.Add(module.Name, candidates.ToArray());
        }
        foreach (var group in model.ModuleScopes.GroupBy(scope => scope.File, result._scopes.Comparer))
            result._scopes.Add(
                group.Key,
                [
                    .. group
                        .GroupBy(scope => scope.Context)
                        .Select(context =>
                            context.OrderBy(scope => scope.Offset).Select(scope => (scope.Offset, scope.Module)).ToArray()
                        ),
                ]
            );
        return result;
    }

    /// <summary>Returns candidates for a physical source offset and optional explicit module qualifier.</summary>
    public IReadOnlyList<PrologPredicateCompletion> At(string fileName, int offset, string? module = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (module is not null)
            return _candidates.GetValueOrDefault(module) ?? [];
        if (!_scopes.TryGetValue(fileName, out var contexts))
            return _candidates.GetValueOrDefault(ModuleTable.UserModule) ?? [];
        IEnumerable<PrologPredicateCompletion>? candidates = null;
        foreach (var scopes in contexts)
        {
            string current = ModuleTable.UserModule;
            foreach (var scope in scopes)
            {
                if (scope.Offset > offset)
                    break;
                current = scope.Module;
            }
            var visible = _candidates.GetValueOrDefault(current) ?? [];
            candidates = candidates is null ? visible : candidates.Intersect(visible);
        }
        return candidates?.ToArray() ?? [];
    }

    private static bool IsWord(string name)
    {
        if (name.Length == 0)
            return false;
        int first = CharacterClass.At(name, 0, true).Code;
        if (!CharacterClass.IsIdentifierStart(first) || CharacterClass.IsUpper(first))
            return false;
        for (int index = 0; index < name.Length; )
        {
            var (code, width) = CharacterClass.At(name, index, true);
            if (code != '_' && !CharacterClass.IsIdentifierContinue(code))
                return false;
            index += width;
        }
        return true;
    }
}
