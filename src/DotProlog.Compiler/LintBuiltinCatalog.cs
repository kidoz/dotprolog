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
        ParseResult parsed = TermReader.ReadProgram(source, "bundled-library.pl");
        if (!parsed.Success)
        {
            throw new InvalidOperationException("The bundled Prolog library could not be read for lint signatures.");
        }

        foreach (SyntaxTerm clause in parsed.Clauses)
        {
            var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
            if (LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause), grammar, out PredicateIndicator indicator))
            {
                result.Add(indicator);
            }
        }
    }
}
