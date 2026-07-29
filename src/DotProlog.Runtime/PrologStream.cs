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

    private string _buffer = string.Empty;
    private EndState _endState;

    internal PrologStream(int id, string name, string mode, string? alias, TextReader? reader, TextWriter? writer, bool permanent)
    {
        Id = id;
        Name = name;
        Mode = mode;
        Alias = alias;
        Reader = reader;
        Writer = writer;
        IsPermanent = permanent;
    }

    /// <summary>The stream's identifier, which appears in the <c>'$stream'(N)</c> term.</summary>
    public int Id { get; }

    /// <summary>The file name, or the alias for a standard stream.</summary>
    public string Name { get; }

    /// <summary>The mode used to open the stream: <c>read</c>, <c>write</c>, or <c>append</c>.</summary>
    internal string Mode { get; }

    /// <summary>The alias this stream answers to, if any.</summary>
    public string? Alias { get; internal set; }

    /// <summary>Where this stream reads from, or <see langword="null"/> for an output stream.</summary>
    public TextReader? Reader { get; internal set; }

    /// <summary>Where this stream writes to, or <see langword="null"/> for an input stream.</summary>
    public TextWriter? Writer { get; internal set; }

    /// <summary>Whether this is a standard stream, which cannot be closed.</summary>
    public bool IsPermanent { get; }

    /// <summary>Whether this stream is still open.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>Text read past the end of the last clause, held for the next read.</summary>
    internal ref string Buffer => ref _buffer;

    /// <summary>Returns the live ISO end-of-stream state without consuming input.</summary>
    internal EndState ObserveEnd()
    {
        if (Reader is null || _endState == EndState.Past)
        {
            return _endState;
        }

        // Inspecting an interactive standard input must not wait for a user keystroke merely to
        // answer stream_property/2. A consuming input operation will update the state normally.
        if (ReferenceEquals(Reader, Console.In))
        {
            return EndState.Not;
        }

        return _buffer.Length == 0 && Reader.Peek() < 0 ? EndState.At : EndState.Not;
    }

    /// <summary>Records whether a consuming input operation returned the EOF marker.</summary>
    internal void RecordInput(bool read)
    {
        _endState = read ? EndState.Not : EndState.Past;
    }

    /// <summary>Replaces the standard input source and resets its EOF state.</summary>
    internal void SetReader(TextReader reader)
    {
        Reader = reader;
        _buffer = string.Empty;
        _endState = EndState.Not;
    }

    /// <summary>Closes the stream, releasing whatever it was reading from or writing to.</summary>
    internal void Close()
    {
        if (!IsOpen || IsPermanent)
        {
            return;
        }

        IsOpen = false;
        Reader?.Dispose();
        Writer?.Flush();
        Writer?.Dispose();
        Reader = null;
        Writer = null;
    }
}
