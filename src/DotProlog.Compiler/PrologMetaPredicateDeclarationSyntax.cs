using System.Globalization;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

internal static class PrologMetaPredicateDeclarationSyntax
{
    internal static bool TryNormalize(
        string directiveName,
        SyntaxTerm item,
        out PredicateIndicator indicator,
        out string template,
        out string signature
    )
    {
        indicator = default;
        template = string.Empty;
        signature = string.Empty;
        if (item is not CompoundTerm compound || !LintSyntax.TryPredicate(compound, grammar: false, out indicator))
        {
            return false;
        }

        var modes = new string[compound.Arity];
        for (var index = 0; index < compound.Arguments.Count; index++)
        {
            SyntaxTerm argument = compound.Arguments[index];
            if (directiveName == "meta_predicate")
            {
                modes[index] = argument switch
                {
                    IntegerTerm { Value: >= 0 } extra => "G" + extra.Value.ToString(CultureInfo.InvariantCulture),
                    AtomTerm { Name: ":" } => "M",
                    _ => "O",
                };
                continue;
            }

            if (directiveName != "metapredicate")
            {
                return false;
            }

            if (argument is not AtomTerm { Name: ":" or "*" } mode)
            {
                return false;
            }

            modes[index] = mode.Name == ":" ? "M" : "O";
        }

        template = $"{compound.Name}({string.Join(", ", compound.Arguments.Select(DisplayArgument))})";
        signature = string.Join(',', modes);
        return true;
    }

    private static string DisplayArgument(SyntaxTerm argument) =>
        argument switch
        {
            AtomTerm atom => atom.Name,
            IntegerTerm integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            VariableTerm variable => variable.Name,
            _ => LintSyntax.CanonicalForm(argument),
        };
}
