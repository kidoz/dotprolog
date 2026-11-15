using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;

namespace DotProlog.LanguageServer;

// A bad frame cannot be resynchronized safely. Bound both phases before retaining a complete
// message, and let the server close the session with a protocol error on framing failures.
internal sealed class LspMessageReader(Stream input) : IAsyncDisposable
{
    internal const int MaximumHeaderBytes = 8 * 1024;
    internal const int MaximumBodyBytes = 8 * 1024 * 1024;

    private readonly PipeReader _reader = PipeReader.Create(input, new StreamPipeReaderOptions(leaveOpen: true));
    private readonly LspMessageFormatter _formatter = new();

    internal async ValueTask<LspMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        int length;
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (TryReadHeader(buffer, out var header, out var end))
            {
                try
                {
                    length = ParseLength(header);
                }
                finally
                {
                    _reader.AdvanceTo(end);
                }
                break;
            }
            _reader.AdvanceTo(buffer.Start, buffer.End);
            if (buffer.Length >= MaximumHeaderBytes)
                throw new InvalidDataException("LSP headers exceed the 8 KiB limit.");
            if (result.IsCompleted)
            {
                if (buffer.IsEmpty)
                    return null;
                throw new InvalidDataException("Incomplete LSP header.");
            }
        }

        if (length == 0)
            return (LspMessage)_formatter.Deserialize(ReadOnlySequence<byte>.Empty);
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (buffer.Length >= length)
            {
                var body = buffer.Slice(0, length);
                try
                {
                    return (LspMessage)_formatter.Deserialize(body);
                }
                finally
                {
                    _reader.AdvanceTo(body.End);
                }
            }
            _reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
                throw new InvalidDataException("Incomplete LSP body.");
        }
    }

    public ValueTask DisposeAsync() => _reader.CompleteAsync();

    private static bool TryReadHeader(ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> header, out SequencePosition end)
    {
        var reader = new SequenceReader<byte>(buffer);
        var found = reader.TryReadTo(out header, "\r\n\r\n"u8);
        end = reader.Position;
        return found;
    }

    private static int ParseLength(ReadOnlySequence<byte> header)
    {
        if (header.Length + 4 > MaximumHeaderBytes)
            throw new InvalidDataException("LSP headers exceed the 8 KiB limit.");
        var length = -1;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.None))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new InvalidDataException("Malformed LSP header.");
            var name = line.AsSpan(0, separator);
            var value = line.AsSpan(separator + 1).Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (
                    length != -1
                    || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length)
                    || length > MaximumBodyBytes
                )
                    throw new InvalidDataException("LSP Content-Length must be a single non-negative integer at most 8 MiB.");
            }
            else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var parameter in value.ToString().Split(';').Skip(1))
                {
                    var pair = parameter.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (
                        pair.Length == 2
                        && pair[0].Equals("charset", StringComparison.OrdinalIgnoreCase)
                        && !pair[1].Equals("utf-8", StringComparison.OrdinalIgnoreCase)
                        && !pair[1].Equals("utf8", StringComparison.OrdinalIgnoreCase)
                    )
                        throw new InvalidDataException("LSP requires UTF-8.");
                }
            }
        }
        if (length == -1)
            throw new InvalidDataException("Missing LSP Content-Length.");
        return length;
    }
}
