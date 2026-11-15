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
        UserInput = Add(
            new PrologStream(
                0,
                "user_input",
                "read",
                "text",
                "user_input",
                Console.In,
                null,
                null,
                reposition: false,
                permanent: true,
                PrologStream.EofAction.Reset
            )
        );
        UserOutput = Add(
            new PrologStream(
                1,
                "user_output",
                "append",
                "text",
                "user_output",
                null,
                Console.Out,
                null,
                reposition: false,
                permanent: true,
                PrologStream.EofAction.Reset
            )
        );
        UserError = Add(
            new PrologStream(
                2,
                "user_error",
                "append",
                "text",
                "user_error",
                null,
                Console.Error,
                null,
                reposition: false,
                permanent: true,
                PrologStream.EofAction.Reset
            )
        );

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
    /// <param name="type">Either <c>text</c> or <c>binary</c>.</param>
    /// <param name="reposition">Whether restoring a captured position is permitted.</param>
    /// <exception cref="IOException">The file could not be opened.</exception>
    public PrologStream Open(string path, string mode, string? alias, string type, bool reposition) =>
        Open(path, mode, alias, type, reposition, PrologStream.EofAction.EofCode);

    /// <summary>Opens and registers a stream with an explicit ISO end-of-file action.</summary>
    internal PrologStream Open(
        string path,
        string mode,
        string? alias,
        string type,
        bool reposition,
        PrologStream.EofAction eofAction,
        bool requireReposition = false
    )
    {
        ArgumentNullException.ThrowIfNull(path);

        if (alias is not null && ByAlias(alias) is not null)
        {
            throw new InvalidOperationException($"The stream alias '{alias}' is already in use.");
        }

        var stream =
            type == "binary"
                ? OpenBinary(path, mode, alias, reposition, eofAction)
                : OpenText(path, mode, alias, reposition, eofAction);
        if (requireReposition && !stream.Reposition)
        {
            stream.Close(force: true);
            throw new IOException("The stream cannot be repositioned.");
        }

        return Add(stream);
    }

    private PrologStream OpenText(string path, string mode, string? alias, bool reposition, PrologStream.EofAction eofAction)
    {
        if (mode == "read")
        {
            var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var reader = new PositionedTextReader(input);
            return new PrologStream(
                _streams.Count,
                path,
                mode,
                "text",
                alias,
                reader,
                null,
                null,
                reposition && reader.CanReposition,
                permanent: false,
                eofAction
            );
        }

        return new PrologStream(
            _streams.Count,
            path,
            mode,
            "text",
            alias,
            null,
            mode switch
            {
                "write" => new PositionedTextWriter(path, append: false),
                "append" => new PositionedTextWriter(path, append: true),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown stream mode."),
            },
            null,
            reposition,
            false,
            eofAction
        );
    }

    private PrologStream OpenBinary(string path, string mode, string? alias, bool reposition, PrologStream.EofAction eofAction)
    {
        var stream = mode switch
        {
            "read" => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            "write" => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            "append" => OpenBinaryAppend(path),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown stream mode."),
        };

        return new PrologStream(
            _streams.Count,
            path,
            mode,
            "binary",
            alias,
            null,
            null,
            stream,
            reposition && stream.CanSeek,
            permanent: false,
            eofAction
        );
    }

    /// <summary>Closes <paramref name="stream"/> and points anything that was using it back at the standard streams.</summary>
    public void Close(PrologStream stream, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.Close(force))
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
    }

    /// <summary>Finds an open stream by identifier.</summary>
    public PrologStream? ById(int id) => id >= 0 && id < _streams.Count && _streams[id].IsOpen ? _streams[id] : null;

    /// <summary>Number of stream identifiers allocated so far, including closed streams.</summary>
    internal int Count => _streams.Count;

    /// <summary>Finds an open stream by alias.</summary>
    public PrologStream? ByAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return _aliases.TryGetValue(alias, out var stream) && stream.IsOpen ? stream : null;
    }

    /// <summary>Closes every stream a program opened, leaving the standard ones.</summary>
    public void CloseAll()
    {
        foreach (var stream in _streams)
        {
            if (!stream.IsPermanent && stream.IsOpen)
            {
                Close(stream, force: true);
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
        CurrentOutput = new PrologStream(
            -1,
            "with_output_to",
            "write",
            "text",
            null,
            null,
            sink,
            null,
            reposition: false,
            permanent: false
        );
    }

    /// <summary>Stops capturing and returns what was written.</summary>
    public string EndCapture()
    {
        if (_captures.Count == 0)
        {
            return string.Empty;
        }

        (var previous, var sink) = _captures.Pop();
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

    private static FileStream OpenBinaryAppend(string path)
    {
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        stream.Position = stream.Length;
        return stream;
    }
}
