using System.Text;

namespace DotProlog.Runtime;

/// <summary>A UTF-8 text-file writer whose flushed byte position can be restored.</summary>
internal sealed class PositionedTextWriter : TextWriter
{
    private readonly FileStream _stream;
    private StreamWriter _writer;

    internal PositionedTextWriter(string path, bool append)
    {
        _stream = new FileStream(path, append ? FileMode.OpenOrCreate : FileMode.Create, FileAccess.Write, FileShare.Read);

        if (append)
        {
            _stream.Position = _stream.Length;
        }

        _writer = CreateWriter();
    }

    public override Encoding Encoding => _writer.Encoding;

    internal long Position
    {
        get
        {
            Flush();
            return _stream.Position;
        }
    }

    public override void Write(char value) => _writer.Write(value);

    public override void Write(char[] buffer, int index, int count) => _writer.Write(buffer, index, count);

    public override void Write(ReadOnlySpan<char> buffer) => _writer.Write(buffer);

    public override void Write(string? value) => _writer.Write(value);

    public override void Flush() => _writer.Flush();

    internal bool TrySeek(long position)
    {
        if (position < 0)
        {
            return false;
        }

        _writer.Flush();
        _writer.Dispose();
        _stream.Position = position;
        _writer = CreateWriter();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer.Dispose();
            _stream.Dispose();
        }

        base.Dispose(disposing);
    }

    private StreamWriter CreateWriter() =>
        new(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true);
}
