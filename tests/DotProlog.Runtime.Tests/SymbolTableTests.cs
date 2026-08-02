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
}
