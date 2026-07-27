using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class CellTests
{
    [Fact]
    public void CellIsEightBytes()
    {
        Assert.Equal(8, System.Runtime.CompilerServices.Unsafe.SizeOf<Cell>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1234567)]
    [InlineData((int)((1L << 30) - 1))]
    public void ReferenceRoundTripsItsAddress(int address)
    {
        Cell cell = Cell.Reference(address);

        Assert.Equal(CellTag.Reference, cell.Tag);
        Assert.Equal(address, cell.Index);
        Assert.True(cell.IsReference);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(42L)]
    [InlineData(-42L)]
    [InlineData(Cell.MaxInteger)]
    [InlineData(Cell.MinInteger)]
    public void IntegerRoundTripsIncludingNegatives(long value)
    {
        Cell cell = Cell.Integer60(value);

        Assert.Equal(CellTag.Integer, cell.Tag);
        Assert.Equal(value, cell.Integer);
    }

    [Fact]
    public void IntegerOutsideSixtyBitsIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Cell.Integer60(Cell.MaxInteger + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cell.Integer60(Cell.MinInteger - 1));
        Assert.False(Cell.FitsInteger(long.MaxValue));
        Assert.True(Cell.FitsInteger(Cell.MaxInteger));
    }

    [Fact]
    public void CellsWithTheSameTagAndPayloadAreEqual()
    {
        Assert.Equal(Cell.Atom(7), Cell.Atom(7));
        Assert.NotEqual(Cell.Atom(7), Cell.Atom(8));

        // A tag is part of identity: atom 7 and structure at address 7 are different terms.
        Assert.NotEqual(Cell.Atom(7), Cell.Structure(7));
    }
}
