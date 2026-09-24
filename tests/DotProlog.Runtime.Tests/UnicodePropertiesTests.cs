using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>
/// The Unicode property tables are ascending, disjoint, inclusive ranges in pairs, which the binary
/// search in <see cref="UnicodeProperties.Contains"/> relies on; regenerating them must keep that shape.
/// </summary>
public sealed class UnicodePropertiesTests
{
    public static TheoryData<string> Tables =>
        ["OtherAlphabetic", "OtherUppercase", "OtherLowercase", "OtherIdentifierStart", "OtherIdentifierContinue"];

    [Theory]
    [MemberData(nameof(Tables))]
    public void RangesAreAscendingDisjointPairs(string table)
    {
        ReadOnlySpan<int> ranges = RangesOf(table);

        Assert.True(ranges.Length > 0 && ranges.Length % 2 == 0);
        for (var index = 0; index < ranges.Length; index += 2)
        {
            Assert.True(ranges[index] <= ranges[index + 1]);
            Assert.True(index == 0 || ranges[index] > ranges[index - 1] + 1);
        }
    }

    [Theory]
    [InlineData("OtherAlphabetic", 0x093F, true)]
    [InlineData("OtherAlphabetic", 0x0939, false)]
    [InlineData("OtherUppercase", 0x2160, true)]
    [InlineData("OtherUppercase", 0x24CF, true)]
    [InlineData("OtherUppercase", 0x24D0, false)]
    [InlineData("OtherLowercase", 0x00AA, true)]
    [InlineData("OtherLowercase", 0x0061, false)]
    [InlineData("OtherIdentifierStart", 0x2118, true)]
    [InlineData("OtherIdentifierContinue", 0x00B7, true)]
    [InlineData("OtherIdentifierContinue", 0x10FFFF, false)]
    public void ContainsFindsTheRangeMembers(string table, int code, bool expected) =>
        Assert.Equal(expected, UnicodeProperties.Contains(RangesOf(table), code));

    private static ReadOnlySpan<int> RangesOf(string table) =>
        table switch
        {
            "OtherAlphabetic" => UnicodeProperties.OtherAlphabetic,
            "OtherUppercase" => UnicodeProperties.OtherUppercase,
            "OtherLowercase" => UnicodeProperties.OtherLowercase,
            "OtherIdentifierStart" => UnicodeProperties.OtherIdentifierStart,
            _ => UnicodeProperties.OtherIdentifierContinue,
        };
}
