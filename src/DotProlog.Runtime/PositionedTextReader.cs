using System.Text;

namespace DotProlog.Runtime;

/// <summary>An incremental UTF-8 reader whose next logical character has a restorable position.</summary>
internal sealed class PositionedTextReader : TextReader
{
    private readonly Stream _stream;
    private readonly byte[] _bytes = new byte[4096];
    private int _bytePosition;
    private int _byteCount;
    private int _peeked = -1;
    private int _pendingLowSurrogate = -1;
    private bool _atStart = true;
    private long _position;

    internal PositionedTextReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    internal long Position => _position;

    internal bool CanReposition => _stream.CanSeek;

    public override int Peek()
    {
        if (_peeked < 0)
        {
            _peeked = ReadCharacter();
        }

        return _peeked;
    }

    public override int Read()
    {
        int next;
        if (_peeked >= 0)
        {
            next = _peeked;
            _peeked = -1;
        }
        else
        {
            next = ReadCharacter();
        }

        if (next >= 0)
        {
            _position++;
        }

        return next;
    }

    public override string? ReadLine()
    {
        var first = Read();
        if (first < 0)
        {
            return null;
        }

        var line = new StringBuilder();
        var current = (char)first;
        while (true)
        {
            if (current is '\r' or '\n')
            {
                if (current == '\r' && Peek() == '\n')
                {
                    _ = Read();
                }

                return line.ToString();
            }

            line.Append(current);
            var next = Read();
            if (next < 0)
            {
                return line.ToString();
            }

            current = (char)next;
        }
    }

    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(index, count));
    }

    public override int Read(Span<char> buffer)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var next = Read();
            if (next < 0)
            {
                break;
            }

            buffer[count++] = (char)next;
        }

        return count;
    }

    internal bool TrySeek(long position)
    {
        if (!CanReposition || position < 0 || position > _stream.Length)
        {
            return false;
        }

        var original = _position;
        Rewind();
        while (_position < position)
        {
            if (Read() < 0)
            {
                Rewind();
                while (_position < original)
                {
                    _ = Read();
                }

                return false;
            }
        }

        return true;
    }

    internal long PositionBeforeBuffer(string buffer) => _position - buffer.Length;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Rewind()
    {
        _stream.Position = 0;
        _bytePosition = 0;
        _byteCount = 0;
        _peeked = -1;
        _pendingLowSurrogate = -1;
        _atStart = true;
        _position = 0;
    }

    private int ReadCharacter()
    {
        var character = DecodeCharacter();
        if (_atStart)
        {
            _atStart = false;
            if (character == '\uFEFF')
            {
                character = DecodeCharacter();
            }
        }

        return character;
    }

    private int DecodeCharacter()
    {
        if (_pendingLowSurrogate >= 0)
        {
            var low = _pendingLowSurrogate;
            _pendingLowSurrogate = -1;
            return low;
        }

        var first = ReadByte();
        if (first < 0)
        {
            return -1;
        }

        if (first <= 0x7F)
        {
            return first;
        }

        int continuationCount;
        int code;
        if (first is >= 0xC2 and <= 0xDF)
        {
            continuationCount = 1;
            code = first & 0x1F;
        }
        else if (first is >= 0xE0 and <= 0xEF)
        {
            continuationCount = 2;
            code = first & 0x0F;
        }
        else if (first is >= 0xF0 and <= 0xF4)
        {
            continuationCount = 3;
            code = first & 0x07;
        }
        else
        {
            throw InvalidUtf8();
        }

        for (var index = 0; index < continuationCount; index++)
        {
            var next = ReadByte();
            if (
                next < 0
                || (next & 0xC0) != 0x80
                || (index == 0 && first == 0xE0 && next < 0xA0)
                || (index == 0 && first == 0xED && next > 0x9F)
                || (index == 0 && first == 0xF0 && next < 0x90)
                || (index == 0 && first == 0xF4 && next > 0x8F)
            )
            {
                throw InvalidUtf8();
            }

            code = (code << 6) | (next & 0x3F);
        }

        if (code <= char.MaxValue)
        {
            return code;
        }

        code -= 0x10000;
        _pendingLowSurrogate = 0xDC00 + (code & 0x3FF);
        return 0xD800 + (code >> 10);
    }

    private int ReadByte()
    {
        if (_bytePosition == _byteCount)
        {
            _byteCount = _stream.Read(_bytes, 0, _bytes.Length);
            _bytePosition = 0;
            if (_byteCount == 0)
            {
                return -1;
            }
        }

        return _bytes[_bytePosition++];
    }

    private static DecoderFallbackException InvalidUtf8() => new("The stream contains invalid UTF-8.");
}
