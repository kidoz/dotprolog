namespace DotProlog.Runtime;

/// <summary>
/// Program-owned ISO input-character mappings with immutable versions for stable redo. Characters are
/// given by their codes: Unicode code points in the default mode, and UTF-16 code units — never above
/// 0xFFFF — in strict ISO mode.
/// </summary>
public sealed class CharacterConversionTable
{
    private readonly Dictionary<int, int> _mappings = [];
    private readonly List<Entry[]> _versions =
    [
        [],
    ];

    /// <summary>The immutable mapping version current when this property is read.</summary>
    internal int Version => _versions.Count - 1;

    /// <summary>Maps the code of one unquoted input character through the current table.</summary>
    public int Convert(int input) => _mappings.TryGetValue(input, out var output) ? output : input;

    /// <summary>Sets a mapping between two character codes, removing it when they are identical.</summary>
    public void Set(int input, int output)
    {
        bool changed;
        if (input == output)
        {
            changed = _mappings.Remove(input);
        }
        else
        {
            changed = !_mappings.TryGetValue(input, out var previous) || previous != output;
            _mappings[input] = output;
        }

        if (!changed)
        {
            return;
        }

        _versions.Add([.. _mappings.OrderBy(pair => pair.Key).Select(pair => new Entry(pair.Key, pair.Value))]);
    }

    /// <summary>Returns the immutable entries held by a prior mapping version.</summary>
    internal ReadOnlySpan<Entry> Entries(int version) => _versions[version];

    /// <summary>Creates an independent table containing the current mappings.</summary>
    public CharacterConversionTable Copy()
    {
        var copy = new CharacterConversionTable();
        copy.ReplaceWith(this);
        return copy;
    }

    /// <summary>Replaces every mapping with the mappings from another table.</summary>
    public void ReplaceWith(CharacterConversionTable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _mappings.Clear();
        _versions.Clear();
        _versions.Add([]);
        foreach (Entry entry in source._versions[^1])
        {
            Set(entry.Input, entry.Output);
        }
    }

    /// <summary>Every current non-identity mapping, as character codes in code order.</summary>
    public IReadOnlyList<(int Input, int Output)> All() => [.. _versions[^1].Select(entry => (entry.Input, entry.Output))];

    /// <summary>Removes every character conversion.</summary>
    public void Clear()
    {
        _mappings.Clear();
        _versions.Clear();
        _versions.Add([]);
    }

    /// <summary>One non-identity input-to-output mapping.</summary>
    internal readonly record struct Entry(int Input, int Output);
}
