namespace DotProlog.Runtime;

/// <summary>Program-owned ISO input-character mappings with immutable versions for stable redo.</summary>
public sealed class CharacterConversionTable
{
    private readonly Dictionary<char, char> _mappings = [];
    private readonly List<Entry[]> _versions =
    [
        [],
    ];

    /// <summary>The immutable mapping version current when this property is read.</summary>
    internal int Version => _versions.Count - 1;

    /// <summary>Maps one unquoted input character through the current table.</summary>
    public char Convert(char input) => _mappings.TryGetValue(input, out var output) ? output : input;

    /// <summary>Sets a mapping, removing it when input and output are identical.</summary>
    public void Set(char input, char output)
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

    /// <summary>Every current non-identity mapping in character order.</summary>
    public IReadOnlyList<(char Input, char Output)> All() =>
        [.. _versions[^1].Select(entry => (entry.Input, entry.Output))];

    /// <summary>Removes every character conversion.</summary>
    public void Clear()
    {
        _mappings.Clear();
        _versions.Clear();
        _versions.Add([]);
    }

    /// <summary>One non-identity input-to-output mapping.</summary>
    internal readonly record struct Entry(char Input, char Output);
}
