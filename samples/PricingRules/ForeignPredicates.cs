using DotProlog.Runtime;

namespace PricingRules;

internal static class ForeignPredicates
{
    [PrologPredicate(Name = "foreign_echo", Arity = 1)]
    internal static bool Echo(ref PrologContext context, Cell value)
    {
        context.Machine.Output.WriteLine(context.Format(value));
        return true;
    }
}
