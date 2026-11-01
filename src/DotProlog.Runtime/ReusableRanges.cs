namespace DotProlog.Runtime;

/// <summary>Address-ordered free ranges. Live allocations never move.</summary>
internal sealed class ReusableRanges
{
    private readonly List<(int Start, int Length)> _ranges = [];

    internal int Allocate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        for (var i = 0; i < _ranges.Count; i++)
        {
            var range = _ranges[i];
            if (range.Length < length)
                continue;
            if (range.Length == length)
                _ranges.RemoveAt(i);
            else
                _ranges[i] = (range.Start + length, range.Length - length);
            return range.Start;
        }
        return -1;
    }

    internal int Release(int start, int length, int highWater)
    {
        if (length == 0)
            return highWater;
        if (start < 0 || length < 0 || start > highWater - length)
            throw new InvalidOperationException("A released range must belong to the allocated address space.");

        var position = 0;
        while (position < _ranges.Count && _ranges[position].Start < start)
            position++;

        // Validate before mutating either neighbour, including an adjacent-left/overlapping-right
        // request. A rejected release must leave all existing free ranges available.
        if (
            (position > 0 && _ranges[position - 1].Start + _ranges[position - 1].Length > start)
            || (position < _ranges.Count && start + length > _ranges[position].Start)
        )
            throw new InvalidOperationException("A range cannot be released twice.");

        if (position > 0)
        {
            var previous = _ranges[position - 1];
            var end = previous.Start + previous.Length;
            if (end == start)
            {
                start = previous.Start;
                length += previous.Length;
                _ranges.RemoveAt(--position);
            }
        }
        if (position < _ranges.Count)
        {
            var next = _ranges[position];
            if (start + length == next.Start)
            {
                length += next.Length;
                _ranges.RemoveAt(position);
            }
        }
        _ranges.Insert(position, (start, length));

        while (_ranges.Count > 0)
        {
            var last = _ranges[^1];
            if (last.Start + last.Length != highWater)
                break;
            highWater = last.Start;
            _ranges.RemoveAt(_ranges.Count - 1);
        }
        return highWater;
    }
}
