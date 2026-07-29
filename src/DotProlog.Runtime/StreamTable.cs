namespace DotProlog.Runtime;

/// <summary>
/// The streams a program has open, and which of them <c>read/1</c> and <c>write/1</c> use when no
/// stream is named.
/// </summary>
/// <remarks>
/// The three standard streams exist from the start and cannot be closed. <c>user_output</c> is the
/// one an embedding host sets through <see cref="Machine.Output"/>, so redirecting output inside a
/// program with <c>set_output/1</c> or <c>with_output_to/2</c> never disturbs what the host handed in.
/// </remarks>
public sealed class StreamTable
{
    private readonly List<PrologStream> _streams = [];
    private readonly Dictionary<string, PrologStream> _aliases = new(StringComparer.Ordinal);
    private readonly Stack<(PrologStream Previous, StringWriter Sink)> _captures = new();

    /// <summary>Creates a table holding the three standard streams.</summary>
    public StreamTable()
    {
        UserInput = Add(new PrologStream(0, "user_input", "read", "user_input", Console.In, null, permanent: true));
        UserOutput = Add(new PrologStream(1, "user_output", "write", "user_output", null, Console.Out, permanent: true));
        UserError = Add(new PrologStream(2, "user_error", "write", "user_error", null, Console.Error, permanent: true));

        CurrentInput = UserInput;
        CurrentOutput = UserOutput;
    }

    /// <summary>The standard input stream.</summary>
    public PrologStream UserInput { get; }

    /// <summary>The standard output stream.</summary>
    public PrologStream UserOutput { get; }

    /// <summary>The standard error stream.</summary>
    public PrologStream UserError { get; }

    /// <summary>The stream a read with no stream argument uses.</summary>
    public PrologStream CurrentInput { get; internal set; }

    /// <summary>The stream a write with no stream argument uses.</summary>
    public PrologStream CurrentOutput { get; internal set; }

    /// <summary>Opens <paramref name="path"/> and registers the stream.</summary>
    /// <param name="path">File to open.</param>
    /// <param name="mode">One of <c>read</c>, <c>write</c>, or <c>append</c>.</param>
    /// <param name="alias">An alias to register the stream under, if any.</param>
    /// <exception cref="IOException">The file could not be opened.</exception>
    public PrologStream Open(string path, string mode, string? alias)
    {
        ArgumentNullException.ThrowIfNull(path);

        PrologStream stream = mode switch
        {
            "read" => new PrologStream(_streams.Count, path, mode, alias, new StreamReader(path), null, permanent: false),
            "write" => new PrologStream(_streams.Count, path, mode, alias, null, new StreamWriter(path, append: false), false),
            "append" => new PrologStream(_streams.Count, path, mode, alias, null, new StreamWriter(path, append: true), false),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown stream mode."),
        };

        return Add(stream);
    }

    /// <summary>Closes <paramref name="stream"/> and points anything that was using it back at the standard streams.</summary>
    public void Close(PrologStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.IsPermanent)
        {
            return;
        }

        if (ReferenceEquals(CurrentInput, stream))
        {
            CurrentInput = UserInput;
        }

        if (ReferenceEquals(CurrentOutput, stream))
        {
            CurrentOutput = UserOutput;
        }

        if (stream.Alias is not null)
        {
            _aliases.Remove(stream.Alias);
        }

        stream.Close();
    }

    /// <summary>Finds an open stream by identifier.</summary>
    public PrologStream? ById(int id) => id >= 0 && id < _streams.Count && _streams[id].IsOpen ? _streams[id] : null;

    /// <summary>Number of stream identifiers allocated so far, including closed streams.</summary>
    internal int Count => _streams.Count;

    /// <summary>Finds an open stream by alias.</summary>
    public PrologStream? ByAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return _aliases.TryGetValue(alias, out PrologStream? stream) && stream.IsOpen ? stream : null;
    }

    /// <summary>Closes every stream a program opened, leaving the standard ones.</summary>
    public void CloseAll()
    {
        foreach (PrologStream stream in _streams)
        {
            if (!stream.IsPermanent && stream.IsOpen)
            {
                Close(stream);
            }
        }
    }

    /// <summary>
    /// Sends output to a buffer until <see cref="EndCapture"/>, which is how <c>with_output_to/2</c>
    /// collects what a goal wrote.
    /// </summary>
    /// <remarks>
    /// Captures nest, so a goal run inside one may itself capture. The stack is what makes that work,
    /// and it is why the previous current output is remembered rather than assumed to be user_output.
    /// </remarks>
    public void BeginCapture()
    {
        var sink = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        _captures.Push((CurrentOutput, sink));
        CurrentOutput = new PrologStream(-1, "with_output_to", "write", null, null, sink, permanent: false);
    }

    /// <summary>Stops capturing and returns what was written.</summary>
    public string EndCapture()
    {
        if (_captures.Count == 0)
        {
            return string.Empty;
        }

        (PrologStream previous, StringWriter sink) = _captures.Pop();
        CurrentOutput = previous;
        return sink.ToString();
    }

    private PrologStream Add(PrologStream stream)
    {
        _streams.Add(stream);

        if (stream.Alias is not null)
        {
            _aliases[stream.Alias] = stream;
        }

        return stream;
    }
}
