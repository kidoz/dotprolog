using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The facade shape the contract specifies, hand-written against <see cref="PrologHost"/>.
/// </summary>
/// <remarks>
/// This is the target the code generator has to emit, written out by hand first so the invocation
/// surface is proved before anything generates against it. Each method here corresponds to one row of
/// the determinism table: <c>det</c> returns a value, <c>semidet</c> returns <see langword="bool"/>,
/// and <c>nondet</c> returns an enumerable.
/// </remarks>
public sealed class FacadeShapeTests
{
    private const string PricingSource = """
        :- module(pricing, [discount/3, colour/1, in_stock/1, split/3]).

        discount(Price, Percent, Result) :- Result is Price - (Price * Percent / 100).

        colour(red).
        colour(green).
        colour(blue).

        in_stock(widget).
        in_stock(gadget).

        split([], [], []).
        split([H|T], [H|L], R) :- split(T, L, R).
        split([H|T], L, [H|R]) :- split(T, L, R).
        """;

    /// <summary>The interface a generated facade would declare for the contract above.</summary>
    private interface IPricingModule
    {
        double Discount(double price, long percent);

        bool InStock(string item);

        IEnumerable<string> Colour();

        IEnumerable<(IReadOnlyList<string> Left, IReadOnlyList<string> Right)> Split(IReadOnlyList<string> items);
    }

    /// <summary>The implementation a generated facade would emit.</summary>
    private sealed class PricingModule : IPricingModule
    {
        private readonly PrologHost _host;
        private readonly PrologPredicate _discount;
        private readonly PrologPredicate _colour;
        private readonly PrologPredicate _inStock;
        private readonly PrologPredicate _split;

        internal PricingModule(PrologHost host)
        {
            _host = host;
            _discount = host.Bind("discount", 3);
            _colour = host.Bind("colour", 1);
            _inStock = host.Bind("in_stock", 1);
            _split = host.Bind("split", 3);
        }

        // det: exactly one solution, one output — the output becomes the return value.
        public double Discount(double price, long percent)
        {
            PrologValue[] outputs =
                _host.CallOnce(_discount, PrologInput.Float(price), PrologInput.Integer(percent), PrologInput.Output)
                ?? throw new PrologException("discount/3 was declared det but failed.");

            return ((PrologFloat)outputs[0]).Value;
        }

        // semidet: no outputs — the result is whether it succeeded.
        public bool InStock(string item) => _host.Prove(_inStock, PrologInput.Atom(item));

        // nondet: one output — every solution is yielded, lazily.
        public IEnumerable<string> Colour()
        {
            foreach (PrologValue[] outputs in _host.CallAll(_colour, PrologInput.Output))
            {
                yield return ((PrologAtom)outputs[0]).Name;
            }
        }

        // nondet with several outputs — the generator would emit a record; a tuple stands in here.
        public IEnumerable<(IReadOnlyList<string> Left, IReadOnlyList<string> Right)> Split(IReadOnlyList<string> items)
        {
            PrologInput list = PrologInput.List([.. items.Select(PrologInput.Atom)]);

            foreach (PrologValue[] outputs in _host.CallAll(_split, list, PrologInput.Output, PrologInput.Output))
            {
                yield return (Atoms(outputs[0]), Atoms(outputs[1]));
            }
        }

        private static IReadOnlyList<string> Atoms(PrologValue value)
        {
            Assert.True(value.TryGetList(out IReadOnlyList<PrologValue> items));
            return [.. items.Select(item => ((PrologAtom)item).Name)];
        }
    }

    private static PricingModule NewModule()
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(PricingSource, "pricing.pl");
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));

        return new PricingModule(new PrologHost(engine.Machine));
    }

    [Fact]
    public void DeterministicPredicateReturnsItsOutput()
    {
        IPricingModule module = NewModule();

        Assert.Equal(90.0, module.Discount(100.0, 10));
    }

    [Theory]
    [InlineData("widget", true)]
    [InlineData("gadget", true)]
    [InlineData("sprocket", false)]
    public void SemiDeterministicPredicateReturnsWhetherItHolds(string item, bool expected)
    {
        IPricingModule module = NewModule();

        Assert.Equal(expected, module.InStock(item));
    }

    [Fact]
    public void NondeterministicPredicateYieldsEverySolution()
    {
        IPricingModule module = NewModule();

        Assert.Equal(["red", "green", "blue"], module.Colour());
    }

    [Fact]
    public void NondeterministicPredicateIsEnumeratedLazily()
    {
        IPricingModule module = NewModule();

        Assert.Equal(["red"], module.Colour().Take(1));
    }

    [Fact]
    public void ListsCrossTheBoundaryInBothDirections()
    {
        IPricingModule module = NewModule();

        // Every way of splitting [a,b] into two lists, rendered as "left|right".
        string[] shapes =
        [
            .. module.Split(["a", "b"]).Select(split => $"{string.Concat(split.Left)}|{string.Concat(split.Right)}"),
        ];

        Assert.Equal(["ab|", "a|b", "b|a", "|ab"], shapes);
    }

    [Fact]
    public void BindingAnUndefinedPredicateFailsImmediatelyRatherThanAtCallTime()
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        var host = new PrologHost(engine.Machine);

        PrologException error = Assert.Throws<PrologException>(() => host.Bind("nowhere", 2));

        Assert.Contains("existence_error(procedure, nowhere/2)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CallingWithTheWrongNumberOfArgumentsIsRejected()
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        Assert.True(engine.ConsultText(PricingSource, "pricing.pl").Success);

        var host = new PrologHost(engine.Machine);
        PrologPredicate colour = host.Bind("colour", 1);

        Assert.Throws<PrologException>(() => host.Prove(colour, PrologInput.Output, PrologInput.Output));
    }
}
