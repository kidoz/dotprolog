namespace DotProlog.Runtime;

/// <summary>A text-file reader whose next logical character has an explicit restorable position.</summary>
internal sealed class PositionedTextReader(string text) : TextReader
{
    private int _position;

    internal long Position => _position;

    public override int Peek() => _position < text.Length ? text[_position] : -1;

    public override int Read() => _position < text.Length ? text[_position++] : -1;

    public override string? ReadLine()
    {
        if (_position >= text.Length)
        {
            return null;
        }

        var start = _position;
        while (_position < text.Length && text[_position] is not ('\r' or '\n'))
        {
            _position++;
        }

        var contentEnd = _position;
        if (_position < text.Length && text[_position] == '\r')
        {
            _position++;
        }

        if (_position < text.Length && text[_position] == '\n')
        {
            _position++;
        }

        return text[start..contentEnd];
    }

    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(index, count));
    }

    public override int Read(Span<char> buffer)
    {
        var count = Math.Min(buffer.Length, text.Length - _position);
        text.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    internal bool TrySeek(long position)
    {
        if (position < 0 || position > text.Length)
        {
            return false;
        }

        _position = (int)position;
        return true;
    }

    internal long PositionBeforeBuffer(string buffer) => _position - buffer.Length;
}
