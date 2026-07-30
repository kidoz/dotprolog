namespace DotProlog.Runtime;

/// <summary>
/// One open stream: a source of terms and characters, a destination for them, or the standard
/// streams which are both permanent and never closed.
/// </summary>
/// <remarks>
/// A stream is named to a Prolog program by the term <c>'$stream'(N)</c>, or by an alias atom such
/// as <c>user_output</c>. The identifier is not reused after a close, so a stale handle reports
/// <c>existence_error(stream, S)</c> rather than reaching whatever was opened next.
/// </remarks>
public sealed class PrologStream
{
    internal enum EndState
    {
        Not,
        At,
        Past,
    }

    internal enum EofAction
    {
        Error,
        EofCode,
        Reset,
    }

    private string _buffer = string.Empty;
    private EndState _endState;

    internal PrologStream(
        int id,
        string name,
        string mode,
        string type,
        string? alias,
        TextReader? reader,
        TextWriter? writer,
        Stream? binaryStream,
        bool reposition,
        bool permanent,
        EofAction eofAction = EofAction.EofCode
    )
    {
        Id = id;
        Name = name;
        Mode = mode;
        Type = type;
        Alias = alias;
        Reader = reader;
        Writer = writer;
        BinaryStream = binaryStream;
        Reposition = reposition;
        IsPermanent = permanent;
        EndOfFileAction = eofAction;
    }

    /// <summary>The stream's identifier, which appears in the <c>'$stream'(N)</c> term.</summary>
    public int Id { get; }

    /// <summary>The file name, or the alias for a standard stream.</summary>
    public string Name { get; }

    /// <summary>The mode used to open the stream: <c>read</c>, <c>write</c>, or <c>append</c>.</summary>
    internal string Mode { get; }

    /// <summary>Whether this is a <c>text</c> or <c>binary</c> stream.</summary>
    internal string Type { get; }

    /// <summary>Whether input operations are permitted.</summary>
    internal bool IsInput => Mode == "read";

    /// <summary>The alias this stream answers to, if any.</summary>
    public string? Alias { get; internal set; }

    /// <summary>Where this stream reads from, or <see langword="null"/> for an output stream.</summary>
    public TextReader? Reader { get; internal set; }

    /// <summary>Where this stream writes to, or <see langword="null"/> for an input stream.</summary>
    public TextWriter? Writer { get; internal set; }

    /// <summary>The raw byte stream, or <see langword="null"/> for a text stream.</summary>
    internal Stream? BinaryStream { get; private set; }

    /// <summary>Whether <c>set_stream_position/2</c> may restore this stream.</summary>
    internal bool Reposition { get; }

    /// <summary>What an input operation does after an EOF marker has already been returned.</summary>
    internal EofAction EndOfFileAction { get; }

    /// <summary>Whether this is a standard stream, which cannot be closed.</summary>
    public bool IsPermanent { get; }

    /// <summary>Whether this stream is still open.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>Text read past the end of the last clause, held for the next read.</summary>
    internal ref string Buffer => ref _buffer;

    /// <summary>Whether a consuming input operation has already returned the EOF marker.</summary>
    internal bool IsPastEnd => _endState == EndState.Past;

    /// <summary>Returns the live ISO end-of-stream state without consuming input.</summary>
    internal EndState ObserveEnd()
    {
        if (!IsInput || _endState == EndState.Past)
        {
            return _endState;
        }

        if (BinaryStream is not null)
        {
            return BinaryStream.CanSeek && BinaryStream.Position >= BinaryStream.Length ? EndState.At : EndState.Not;
        }

        // Inspecting an interactive standard input must not wait for a user keystroke merely to
        // answer stream_property/2. A consuming input operation will update the state normally.
        if (ReferenceEquals(Reader, Console.In))
        {
            return EndState.Not;
        }

        return _buffer.Length == 0 && Reader!.Peek() < 0 ? EndState.At : EndState.Not;
    }

    /// <summary>Records whether a consuming input operation returned the EOF marker.</summary>
    internal void RecordInput(bool read)
    {
        _endState = read ? EndState.Not : EndState.Past;
    }

    /// <summary>Lets an EOF-reset stream attempt input from its source again.</summary>
    internal void ResetEnd() => _endState = EndState.Not;

    /// <summary>Gets the logical character or byte offset represented by the current position.</summary>
    internal bool TryGetPosition(out long position)
    {
        if (Reader is PositionedTextReader reader)
        {
            position = reader.PositionBeforeBuffer(_buffer);
            return true;
        }

        if (Writer is PositionedTextWriter writer)
        {
            position = writer.Position;
            return true;
        }

        if (BinaryStream is { CanSeek: true })
        {
            position = BinaryStream.Position;
            return true;
        }

        position = 0;
        return false;
    }

    /// <summary>Restores a logical position and clears input lookahead and EOF state.</summary>
    internal bool TrySetPosition(long position)
    {
        bool changed =
            Reader is PositionedTextReader reader ? reader.TrySeek(position)
            : Writer is PositionedTextWriter writer ? writer.TrySeek(position)
            : TrySeekBinary(position);

        if (changed)
        {
            _buffer = string.Empty;
            _endState = EndState.Not;
        }

        return changed;
    }

    /// <summary>Replaces the standard input source and resets its EOF state.</summary>
    internal void SetReader(TextReader reader)
    {
        Reader = reader;
        _buffer = string.Empty;
        _endState = EndState.Not;
    }

    private bool TrySeekBinary(long position)
    {
        if (BinaryStream is not { CanSeek: true } || position < 0)
        {
            return false;
        }

        BinaryStream.Position = position;
        return true;
    }

    /// <summary>Closes the stream, optionally reclaiming resources despite I/O errors.</summary>
    /// <returns>Whether a non-permanent stream was closed.</returns>
    internal bool Close(bool force)
    {
        if (!IsOpen)
        {
            return true;
        }

        if (IsPermanent)
        {
            if (Writer is not null)
            {
                CloseResource(Writer.Flush, force);
            }

            return false;
        }

        if (force)
        {
            if (Reader is not null)
            {
                CloseResource(Reader.Dispose, force: true);
            }

            if (Writer is not null)
            {
                CloseResource(Writer.Flush, force: true);
                CloseResource(Writer.Dispose, force: true);
            }

            if (BinaryStream is not null)
            {
                CloseResource(BinaryStream.Dispose, force: true);
            }
        }
        else
        {
            Writer?.Flush();
            Reader?.Dispose();
            Writer?.Dispose();
            BinaryStream?.Dispose();
        }

        IsOpen = false;
        Reader = null;
        Writer = null;
        BinaryStream = null;
        return true;
    }

    private static void CloseResource(Action close, bool force)
    {
        try
        {
            close();
        }
        catch (Exception error) when (force && error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // force(true) exists so cleanup code can reclaim the stream despite resource errors.
        }
    }
}
