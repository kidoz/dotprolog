using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// A text indexed by Prolog character. When characters are code points, the positions of the text's
/// supplementary characters — each two UTF-16 code units — map character positions to code units;
/// a text without any, which is nearly every text, maps each position to itself. When characters
/// are UTF-16 code units, as in strict ISO mode, there is never anything to map.
/// </summary>
internal readonly struct CodePointText
{
    /// <summary>The character positions of the supplementary characters, ascending; null when none.</summary>
    private readonly int[]? _supplementary;

    internal CodePointText(string text, int[]? supplementary)
    {
        Text = text;
        _supplementary = supplementary;
    }

    /// <summary>The text as .NET stores it.</summary>
    internal string Text { get; }

    /// <summary>The number of characters.</summary>
    internal int Length => Text.Length - (_supplementary?.Length ?? 0);

    /// <summary>
    /// Indexes text that is not interned. Interned text carries its positions already; see
    /// <see cref="SymbolTable.TextOf(int)"/>.
    /// </summary>
    internal static CodePointText Of(string text, bool codePoints) =>
        codePoints && text.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF')
            ? new CodePointText(text, SupplementaryPositions(text))
            : new CodePointText(text, null);

    /// <summary>The code-unit index where the character at <paramref name="position"/> starts.</summary>
    internal int UnitIndex(int position) => _supplementary is null ? position : position + CountBefore(position);

    /// <summary>The character position of the code-unit index <paramref name="unit"/>, a character boundary.</summary>
    internal int PositionOf(int unit)
    {
        if (_supplementary is null)
        {
            return unit;
        }

        // Supplementary character k starts at code unit _supplementary[k] + k.
        int low = 0,
            high = _supplementary.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_supplementary[middle] + middle < unit)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return unit - low;
    }

    /// <summary>The <paramref name="length"/> characters starting at <paramref name="position"/>.</summary>
    internal string Slice(int position, int length)
    {
        var start = UnitIndex(position);
        return Text[start..UnitIndex(position + length)];
    }

    /// <summary>The code of the character at <paramref name="position"/>.</summary>
    internal int CodeAt(int position)
    {
        var unit = UnitIndex(position);
        return IsPairAt(Text, unit) && _supplementary is not null ? char.ConvertToUtf32(Text[unit], Text[unit + 1]) : Text[unit];
    }

    /// <summary>
    /// Makes <paramref name="text"/> well-formed UTF-16, replacing each unpaired surrogate with U+FFFD,
    /// and returns the character positions of its supplementary characters.
    /// </summary>
    internal static (string Text, int[]? Supplementary) WellFormed(string text)
    {
        StringBuilder? repaired = null;
        List<int> positions = [];
        var position = 0;

        for (var unit = 0; unit < text.Length; unit++, position++)
        {
            if (IsPairAt(text, unit))
            {
                positions.Add(position);
                repaired?.Append(text, unit, 2);
                unit++;
                continue;
            }

            if (char.IsSurrogate(text[unit]))
            {
                repaired ??= new StringBuilder(text, 0, unit, text.Length);
                repaired.Append('\uFFFD');
                continue;
            }

            repaired?.Append(text[unit]);
        }

        return (repaired?.ToString() ?? text, positions.Count == 0 ? null : [.. positions]);
    }

    /// <summary>Whether a surrogate pair starts at <paramref name="unit"/>.</summary>
    internal static bool IsPairAt(string text, int unit) =>
        char.IsHighSurrogate(text[unit]) && unit + 1 < text.Length && char.IsLowSurrogate(text[unit + 1]);

    private static int[]? SupplementaryPositions(string text)
    {
        List<int> positions = [];
        var position = 0;
        for (var unit = 0; unit < text.Length; unit++, position++)
        {
            if (IsPairAt(text, unit))
            {
                positions.Add(position);
                unit++;
            }
        }

        return positions.Count == 0 ? null : [.. positions];
    }

    /// <summary>How many supplementary characters come before character <paramref name="position"/>.</summary>
    private int CountBefore(int position)
    {
        var index = Array.BinarySearch(_supplementary!, position);
        return index >= 0 ? index : ~index;
    }
}
