using PricingRules;

namespace PricingConsole;

/// <summary>
/// A plain C# console application calling a Prolog rule set through an ordinary
/// <c>&lt;ProjectReference&gt;</c> to a <c>.dplproj</c>.
/// </summary>
/// <remarks>
/// Nothing here mentions the engine, goals, or terms. <c>IPricingModule</c> and its methods are
/// generated from <c>pricing.dpli</c> during the build, so the rules are as ordinary to call as any
/// other .NET library — which is what makes the same reference work from F# and VB too.
/// </remarks>
internal static class Program
{
    private static void Main()
    {
        IPricingModule pricing = PricingModule.Create();

        // det: one solution, one output.
        Console.WriteLine($"100 less 15% = {pricing.Discount(100.0, 15)}");

        // det with an atom result.
        foreach (var total in (long[])[1200, 700, 100])
        {
            Console.WriteLine($"total {total} is {pricing.Tier(total)}");
        }

        // semidet: no outputs, so the result is simply whether it holds.
        Console.WriteLine($"widget in catalogue: {pricing.InCatalogue("widget")}");
        Console.WriteLine($"anvil in catalogue:  {pricing.InCatalogue("anvil")}");

        // nondet: every solution, streamed as the loop pulls them.
        Console.WriteLine("bundles of [widget, gadget]:");
        foreach (IReadOnlyList<string> bundle in pricing.Bundle(["widget", "gadget"]))
        {
            Console.WriteLine($"  [{string.Join(", ", bundle)}]");
        }
    }
}
