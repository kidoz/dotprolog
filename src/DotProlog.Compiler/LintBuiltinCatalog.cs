using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Predicate signatures obtained from the actual native registrations and bundled Prolog libraries.</summary>
internal static class LintBuiltinCatalog
{
    private static readonly Lazy<HashSet<PredicateIndicator>> Signatures = new(Build, isThreadSafe: true);
    private static readonly Lazy<HashSet<string>> Names = new(
        () => [.. Signatures.Value.Select(signature => signature.Name)],
        isThreadSafe: true
    );

    internal static bool Contains(PredicateIndicator indicator) => Signatures.Value.Contains(indicator);

    internal static bool ContainsName(string name) => Names.Value.Contains(name);

    internal static IEnumerable<PredicateIndicator> All => Signatures.Value;

    private static HashSet<PredicateIndicator> Build()
    {
        HashSet<PredicateIndicator> result = [];
        var program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(program);
        for (var functorId = 0; functorId < program.Symbols.FunctorCount; functorId++)
        {
            if (program.IsDefined(functorId))
            {
                var functor = program.Symbols.GetFunctor(functorId);
                result.Add(new PredicateIndicator(program.Symbols.AtomName(functor.NameAtom), functor.Arity));
            }
        }

        foreach (var signature in program.Builtins.RegisteredNames)
        {
            var separator = signature.LastIndexOf('/');
            result.Add(
                new PredicateIndicator(
                    signature[..separator],
                    int.Parse(signature[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture)
                )
            );
        }

        AddLibrary(result, BootstrapLibrary.Source);
        AddLibrary(result, StandardLibrary.Source);
        return result;
    }

    private static void AddLibrary(HashSet<PredicateIndicator> result, string source)
    {
        var parsed = TermReader.ReadProgram(source, "bundled-library.pl");
        if (!parsed.Success)
        {
            throw new InvalidOperationException("The bundled Prolog library could not be read for lint signatures.");
        }

        foreach (var clause in parsed.Clauses)
        {
            var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
            if (LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause), grammar, out var indicator))
            {
                result.Add(indicator);
            }
        }
    }
}
