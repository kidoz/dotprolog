using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class SymbolTableTests
{
    [Fact]
    public void InterningTheSameAtomTwiceYieldsTheSameIdentifier()
    {
        var symbols = new SymbolTable();

        var first = symbols.InternAtom("append");
        var second = symbols.InternAtom("append");

        Assert.Equal(first, second);
        Assert.Equal("append", symbols.AtomName(first));
        Assert.NotEqual(first, symbols.InternAtom("appends"));
    }

    [Fact]
    public void CodePointTextIsInternedWellFormed()
    {
        var symbols = new SymbolTable(codePoints: true);

        var repaired = symbols.InternAtom("a\uD800b");

        Assert.Equal("a\uFFFDb", symbols.AtomName(repaired));
        Assert.Equal(repaired, symbols.InternAtom("a\uFFFDb"));
        Assert.Equal("\uD83D\uDE00", symbols.AtomName(symbols.InternAtom("\uD83D\uDE00")));
    }

    [Fact]
    public void CodeUnitTextIsInternedAsGiven()
    {
        var symbols = new SymbolTable(codePoints: false);

        Assert.Equal("a\uD800b", symbols.AtomName(symbols.InternAtom("a\uD800b")));
    }

    [Fact]
    public void FunctorsDifferByArity()
    {
        var symbols = new SymbolTable();

        var binary = symbols.InternFunctor("p", 2);
        var ternary = symbols.InternFunctor("p", 3);

        Assert.NotEqual(binary, ternary);
        Assert.Equal(2, symbols.ArityOf(binary));
        Assert.Equal("p/3", symbols.DescribeFunctor(ternary));
    }

    [Fact]
    public void WellKnownSymbolsArePreInterned()
    {
        var symbols = new SymbolTable();

        Assert.Equal(symbols.EmptyList, symbols.InternAtom("[]"));
        Assert.Equal(symbols.ListFunctor, symbols.InternFunctor(".", 2));
    }

    [Fact]
    public void FloatsAreInternedByValueSoCellEqualityWorks()
    {
        var symbols = new SymbolTable();

        Assert.Equal(symbols.InternFloat(1.5), symbols.InternFloat(1.5));
        Assert.NotEqual(symbols.InternFloat(1.5), symbols.InternFloat(2.5));
        Assert.Equal(1.5, symbols.GetFloat(symbols.InternFloat(1.5)));
    }

    [Fact]
    public void SignedZerosShareOnePositiveZeroEntry()
    {
        var symbols = new SymbolTable();

        var positive = symbols.InternFloat(0.0);
        var negative = symbols.InternFloat(-0.0);

        Assert.Equal(positive, negative);
        Assert.False(double.IsNegative(symbols.GetFloat(negative)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteFloatsAreRejectedAtNumericBoundaries(double value)
    {
        var symbols = new SymbolTable();

        Assert.Throws<ArgumentOutOfRangeException>(() => symbols.InternFloat(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrologNumber.FromReal(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrologInput.Float(value));
    }
}
