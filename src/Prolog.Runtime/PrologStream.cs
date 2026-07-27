namespace Prolog.Runtime;

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
    private string _buffer = string.Empty;

    internal PrologStream(int id, string name, string? alias, TextReader? reader, TextWriter? writer, bool permanent)
    {
        Id = id;
        Name = name;
        Alias = alias;
        Reader = reader;
        Writer = writer;
        IsPermanent = permanent;
    }

    /// <summary>The stream's identifier, which appears in the <c>'$stream'(N)</c> term.</summary>
    public int Id { get; }

    /// <summary>The file name, or the alias for a standard stream.</summary>
    public string Name { get; }

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
