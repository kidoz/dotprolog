namespace DotProlog.Runtime;

/// <summary>
/// The stream predicates: opening and closing, choosing the current stream, reading terms and
/// characters, and writing to a named stream.
/// </summary>
/// <remarks>
/// <para>
/// A stream is named by <c>'$stream'(N)</c> or by an alias atom. Reading a term needs a parser,
/// which the runtime deliberately cannot reference, so it goes out through
/// <see cref="IRuntimeCompiler.TryReadTerm"/> — the same seam <c>assertz/1</c> and <c>consult/1</c>
/// already use.
/// </para>
/// <para>
/// Text streams carry terms and characters, while binary streams carry bytes. Stream positioning
/// remains deliberately absent until opaque ISO positions can be represented consistently.
/// </para>
/// </remarks>
internal static class StreamBuiltins
{
    private const string StreamFunctor = "$stream";
    private const int StreamPropertyCount = 9;

    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("open", 3, static machine => Open(machine, options: -1));
        registry.Register("open", 4, static machine => Open(machine, options: 3));
        registry.Register("close", 1, Close);
        registry.Register("current_input", 1, static machine => Current(machine, input: true));
        registry.Register("current_output", 1, static machine => Current(machine, input: false));
        registry.Register("set_input", 1, static machine => Set(machine, input: true));
        registry.Register("set_output", 1, static machine => Set(machine, input: false));
        registry.RegisterNondeterministic("current_stream", 1, static machine => CurrentStream(machine, 0), CurrentStream);
        registry.RegisterNondeterministic("stream_property", 2, static machine => StreamProperty(machine, 0), StreamProperty);

        registry.Register("read", 1, static machine => Read(machine, stream: -1, term: 0, options: -1));
        registry.Register("read", 2, static machine => Read(machine, stream: 0, term: 1, options: -1));
        registry.Register("read_term", 2, static machine => Read(machine, stream: -1, term: 0, options: 1));
        registry.Register("read_term", 3, static machine => Read(machine, stream: 0, term: 1, options: 2));

        registry.Register("get_char", 1, static machine => GetChar(machine, stream: -1, target: 0, consume: true));
        registry.Register("get_char", 2, static machine => GetChar(machine, stream: 0, target: 1, consume: true));
        registry.Register("peek_char", 1, static machine => GetChar(machine, stream: -1, target: 0, consume: false));
        registry.Register("peek_char", 2, static machine => GetChar(machine, stream: 0, target: 1, consume: false));
        registry.Register("put_char", 1, static machine => PutChar(machine, stream: -1, source: 0));
        registry.Register("put_char", 2, static machine => PutChar(machine, stream: 0, source: 1));
        registry.Register("get_code", 1, static machine => GetCode(machine, stream: -1, target: 0, consume: true));
        registry.Register("get_code", 2, static machine => GetCode(machine, stream: 0, target: 1, consume: true));
        registry.Register("peek_code", 1, static machine => GetCode(machine, stream: -1, target: 0, consume: false));
        registry.Register("peek_code", 2, static machine => GetCode(machine, stream: 0, target: 1, consume: false));
        registry.Register("put_code", 1, static machine => PutCode(machine, stream: -1, source: 0));
        registry.Register("put_code", 2, static machine => PutCode(machine, stream: 0, source: 1));
        registry.Register("get_byte", 1, static machine => GetByte(machine, stream: -1, target: 0, consume: true));
        registry.Register("get_byte", 2, static machine => GetByte(machine, stream: 0, target: 1, consume: true));
        registry.Register("peek_byte", 1, static machine => GetByte(machine, stream: -1, target: 0, consume: false));
        registry.Register("peek_byte", 2, static machine => GetByte(machine, stream: 0, target: 1, consume: false));
        registry.Register("put_byte", 1, static machine => PutByte(machine, stream: -1, source: 0));
        registry.Register("put_byte", 2, static machine => PutByte(machine, stream: 0, source: 1));

        registry.Register("at_end_of_stream", 0, static machine => AtEnd(machine, stream: -1));
        registry.Register("at_end_of_stream", 1, static machine => AtEnd(machine, stream: 0));
        registry.Register("set_stream_position", 2, SetStreamPosition);

        registry.Register("nl", 1, static machine => WriteText(machine, stream: 0, "\n"));
        registry.Register("write", 2, static machine => WriteTerm(machine, stream: 0, term: 1, false, false));
        registry.Register("print", 2, static machine => WriteTerm(machine, stream: 0, term: 1, false, false));
        registry.Register("writeq", 2, static machine => WriteTerm(machine, stream: 0, term: 1, true, false));
        registry.Register("write_canonical", 2, static machine => WriteTerm(machine, stream: 0, term: 1, true, true));
        registry.Register("flush_output", 0, static machine => Flush(machine, stream: -1));
        registry.Register("flush_output", 1, static machine => Flush(machine, stream: 0));

        // Reading a term out of an atom, which is read_term_from_atom/3 without a stream.
        registry.Register("$read_from_atom", 3, ReadFromAtom);

        // The two halves of with_output_to/2; see the standard library for how they are used.
        registry.Register("$capture_begin", 0, CaptureBegin);
        registry.Register("$capture_end", 1, CaptureEnd);
    }

    /// <summary>Builds the term that names <paramref name="stream"/> to a program.</summary>
    private static Cell StreamTerm(Machine machine, PrologStream stream) =>
        machine.CreateStructure(machine.Symbols.InternFunctor(StreamFunctor, 1), [Cell.Integer60(stream.Id)]);

    /// <summary>
    /// Resolves a stream argument, which may be <c>'$stream'(N)</c>, an alias, or absent — in which
    /// case the current input or output is used.
    /// </summary>
    private static PrologStream Resolve(Machine machine, int index, bool input, string? expectedType = "text")
    {
        PrologStream stream;
        if (index < 0)
        {
            stream = input ? machine.Streams.CurrentInput : machine.Streams.CurrentOutput;
        }
        else
        {
            Cell cell = machine.Argument(index);

            if (cell.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            PrologStream? resolved;

            if (cell.Tag == CellTag.Atom)
            {
                resolved = machine.Streams.ByAlias(machine.Symbols.AtomName(cell.Index));
            }
            else if (cell.Tag == CellTag.Structure)
            {
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);

                if (machine.Symbols.AtomName(functor.NameAtom) != StreamFunctor || functor.Arity != 1)
                {
                    throw PrologErrors.Domain(machine, "stream_or_alias", cell);
                }

                Cell id = machine.Dereference(machine.HeapAt(cell.Index + 1));
                if (id.Tag != CellTag.Integer)
                {
                    throw PrologErrors.Domain(machine, "stream_or_alias", cell);
                }

                resolved = machine.Streams.ById((int)id.Integer);
            }
            else
            {
                throw PrologErrors.Domain(machine, "stream_or_alias", cell);
            }

            stream = resolved ?? throw Existence(machine, "stream", cell);
        }

        if (input != stream.IsInput)
        {
            throw PrologErrors.Permission(
                machine,
                input ? "input" : "output",
                "stream",
                machine.Symbols.InternFunctor(stream.Alias ?? stream.Name, 0)
            );
        }

        if (expectedType is not null && stream.Type != expectedType)
        {
            throw PrologErrors.Permission(
                machine,
                input ? "input" : "output",
                stream.Type == "binary" ? "binary_stream" : "text_stream",
                machine.Symbols.InternFunctor(stream.Alias ?? stream.Name, 0)
            );
        }

        return stream;
    }

    private static PrologException Existence(Machine machine, string kind, Cell culprit)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("existence_error", 2),
            [Cell.Atom(machine.Symbols.InternAtom(kind)), culprit]
        );

        Cell error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
        return machine.CreateBall(
            error,
            $"existence_error({kind}, {TermWriter.ToDisplayString(machine, culprit, quoted: true)})"
        );
    }

    private static bool Open(Machine machine, int options)
    {
        Cell file = machine.Argument(0);
        Cell mode = machine.Argument(1);

        if (file.Tag == CellTag.Reference || mode.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (file.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", file);
        }

        if (mode.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", mode);
        }

        string modeName = machine.Symbols.AtomName(mode.Index);
        if (modeName is not ("read" or "write" or "append"))
        {
            throw PrologErrors.Domain(machine, "io_mode", mode);
        }

        OpenOptions openOptions = options < 0 ? new OpenOptions(null, "text", true) : ReadOpenOptions(machine, options);
        string path = machine.Symbols.AtomName(file.Index);

        PrologStream stream;
        try
        {
            stream = machine.Streams.Open(path, modeName, openOptions.Alias, openOptions.Type, openOptions.Reposition);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Existence(machine, "source_sink", file);
        }

        return machine.Unify(machine.Argument(2), StreamTerm(machine, stream));
    }

    /// <summary>Reads the supported ISO options of <c>open/4</c>; unknown options are rejected.</summary>
    private static OpenOptions ReadOpenOptions(Machine machine, int options)
    {
        var result = new OpenOptions(null, "text", true);

        foreach (Cell element in TermList.ReadProper(machine, machine.Argument(options)))
        {
            Cell option = machine.Dereference(element);

            if (option.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (option.Tag != CellTag.Structure || machine.Symbols.ArityOf(machine.HeapAt(option.Index).Index) != 1)
            {
                throw PrologErrors.Domain(machine, "stream_option", option);
            }

            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(option.Index).Index);
            Cell value = machine.Dereference(machine.HeapAt(option.Index + 1));
            string name = machine.Symbols.AtomName(functor.NameAtom);

            if (value.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (value.Tag != CellTag.Atom)
            {
                throw PrologErrors.Domain(machine, "stream_option", option);
            }

            string atom = machine.Symbols.AtomName(value.Index);
            switch (name)
            {
                case "alias":
                    result = result with { Alias = atom };
                    break;
                case "type" when atom is "text" or "binary":
                    result = result with { Type = atom };
                    break;
                case "reposition" when atom is "true" or "false":
                    result = result with { Reposition = atom == "true" };
                    break;
                default:
                    throw PrologErrors.Domain(machine, "stream_option", option);
            }
        }

        return result;
    }

    private readonly record struct OpenOptions(string? Alias, string Type, bool Reposition);

    private static bool Close(Machine machine)
    {
        machine.Streams.Close(ResolveEither(machine, 0));
        return true;
    }

    /// <summary>Resolves a stream argument for an operation that does not care about its direction.</summary>
    private static PrologStream ResolveEither(Machine machine, int index)
    {
        Cell cell = machine.Argument(index);

        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        PrologStream? stream = cell.Tag switch
        {
            CellTag.Atom => machine.Streams.ByAlias(machine.Symbols.AtomName(cell.Index)),
            CellTag.Structure when TryStreamHandle(machine, cell, out int id) => machine.Streams.ById(id),
            CellTag.Structure => throw PrologErrors.Domain(machine, "stream_or_alias", cell),
            _ => throw PrologErrors.Domain(machine, "stream_or_alias", cell),
        };

        return stream ?? throw Existence(machine, "stream", cell);
    }

    private static bool Current(Machine machine, bool input)
    {
        PrologStream stream = input ? machine.Streams.CurrentInput : machine.Streams.CurrentOutput;
        return machine.Unify(machine.Argument(0), StreamTerm(machine, stream));
    }

    private static bool Set(Machine machine, bool input)
    {
        PrologStream stream = Resolve(machine, 0, input, expectedType: null);

        if (input)
        {
            machine.Streams.CurrentInput = stream;
        }
        else
        {
            machine.Streams.CurrentOutput = stream;
        }

        return true;
    }

    private static bool CurrentStream(Machine machine, long state)
    {
        Cell pattern = machine.Argument(0);
        if (pattern.Tag != CellTag.Reference && !TryStreamHandle(machine, pattern, out _))
        {
            throw PrologErrors.Domain(machine, "stream", pattern);
        }

        for (int id = (int)state; id < machine.Streams.Count; id++)
        {
            PrologStream? stream = machine.Streams.ById(id);
            if (stream is null)
            {
                continue;
            }

            Cell candidate = StreamTerm(machine, stream);
            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (id + 1 < machine.Streams.Count)
            {
                machine.PushRetry(id + 1);
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static bool StreamProperty(Machine machine, long state)
    {
        Cell streamPattern = machine.Argument(0);
        Cell propertyPattern = machine.Argument(1);
        PrologStream? selected = ResolvePropertyStream(machine, streamPattern);
        ValidateProperty(machine, propertyPattern);

        int limit = machine.Streams.Count * StreamPropertyCount;
        for (int encoded = (int)state; encoded < limit; encoded++)
        {
            int id = encoded / StreamPropertyCount;
            int property = encoded % StreamPropertyCount;
            PrologStream? stream = machine.Streams.ById(id);

            if (stream is null || (selected is not null && !ReferenceEquals(selected, stream)))
            {
                continue;
            }

            if (!TryProperty(machine, stream, property, out Cell candidateProperty))
            {
                continue;
            }

            Cell candidateStream = StreamTerm(machine, stream);
            Cell pattern;
            Cell candidate;

            if (streamPattern.Tag == CellTag.Reference)
            {
                int pair = machine.Symbols.InternFunctor("-", 2);
                pattern = machine.CreateStructure(pair, [streamPattern, propertyPattern]);
                candidate = machine.CreateStructure(pair, [candidateStream, candidateProperty]);
            }
            else
            {
                pattern = propertyPattern;
                candidate = candidateProperty;
            }

            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (encoded + 1 < limit)
            {
                machine.PushRetry(encoded + 1);
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static PrologStream? ResolvePropertyStream(Machine machine, Cell stream)
    {
        if (stream.Tag == CellTag.Reference)
        {
            return null;
        }

        if (stream.Tag == CellTag.Atom)
        {
            return machine.Streams.ByAlias(machine.Symbols.AtomName(stream.Index)) ?? throw Existence(machine, "stream", stream);
        }

        if (!TryStreamHandle(machine, stream, out int id))
        {
            throw PrologErrors.Domain(machine, "stream", stream);
        }

        return machine.Streams.ById(id) ?? throw Existence(machine, "stream", stream);
    }

    private static bool TryStreamHandle(Machine machine, Cell cell, out int id)
    {
        id = -1;
        if (cell.Tag != CellTag.Structure)
        {
            return false;
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);
        if (machine.Symbols.AtomName(functor.NameAtom) != StreamFunctor || functor.Arity != 1)
        {
            return false;
        }

        Cell argument = machine.Dereference(machine.HeapAt(cell.Index + 1));
        if (argument.Tag != CellTag.Integer || argument.Integer < 0 || argument.Integer > int.MaxValue)
        {
            return false;
        }

        id = (int)argument.Integer;
        return true;
    }

    private static void ValidateProperty(Machine machine, Cell property)
    {
        if (property.Tag == CellTag.Reference || IsDirectionProperty(machine, property))
        {
            return;
        }

        if (property.Tag != CellTag.Structure)
        {
            throw PrologErrors.Domain(machine, "stream_property", property);
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(property.Index).Index);
        string name = machine.Symbols.AtomName(functor.NameAtom);
        if (
            functor.Arity != 1
            || name
                is not (
                    "file_name"
                    or "mode"
                    or "alias"
                    or "type"
                    or "reposition"
                    or "eof_action"
                    or "end_of_stream"
                    or "position"
                )
        )
        {
            throw PrologErrors.Domain(machine, "stream_property", property);
        }

        Cell value = machine.Dereference(machine.HeapAt(property.Index + 1));
        if (name == "position")
        {
            if (value.Tag != CellTag.Reference && !TryReadPosition(machine, value, out _))
            {
                throw PrologErrors.Domain(machine, "stream_property", property);
            }

            return;
        }

        if (value.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", value);
        }
    }

    private static bool IsDirectionProperty(Machine machine, Cell property) =>
        property.Tag == CellTag.Atom && machine.Symbols.AtomName(property.Index) is "input" or "output";

    private static bool TryProperty(Machine machine, PrologStream stream, int property, out Cell candidate)
    {
        candidate = default;
        switch (property)
        {
            case 0:
                candidate = UnaryProperty(machine, "file_name", stream.Name);
                return true;
            case 1:
                candidate = UnaryProperty(machine, "mode", stream.Mode);
                return true;
            case 2:
                candidate = Cell.Atom(machine.Symbols.InternAtom(stream.IsInput ? "input" : "output"));
                return true;
            case 3 when stream.Alias is not null:
                candidate = UnaryProperty(machine, "alias", stream.Alias);
                return true;
            case 4:
                candidate = UnaryProperty(machine, "type", stream.Type);
                return true;
            case 5:
                candidate = UnaryProperty(machine, "reposition", stream.Reposition ? "true" : "false");
                return true;
            case 6 when stream.IsInput:
                candidate = UnaryProperty(machine, "eof_action", "eof_code");
                return true;
            case 7 when stream.IsInput:
                candidate = UnaryProperty(machine, "end_of_stream", EndStateName(stream.ObserveEnd()));
                return true;
            case 8 when stream.TryGetPosition(out long position):
                candidate = PositionProperty(machine, position);
                return true;
            default:
                return false;
        }
    }

    private static Cell UnaryProperty(Machine machine, string name, string value) =>
        machine.CreateStructure(machine.Symbols.InternFunctor(name, 1), [Cell.Atom(machine.Symbols.InternAtom(value))]);

    private static Cell PositionProperty(Machine machine, long position) =>
        machine.CreateStructure(machine.Symbols.InternFunctor("position", 1), [PositionTerm(machine, position)]);

    private static Cell PositionTerm(Machine machine, long position) =>
        machine.CreateStructure(
            machine.Symbols.InternFunctor("$stream_position", 4),
            [Cell.Integer60(position), Cell.Integer60(0), Cell.Integer60(0), Cell.Integer60(0)]
        );

    private static string EndStateName(PrologStream.EndState state) =>
        state switch
        {
            PrologStream.EndState.At => "at",
            PrologStream.EndState.Past => "past",
            _ => "not",
        };

    private static bool Read(Machine machine, int stream, int term, int options)
    {
        PrologStream source = Resolve(machine, stream, input: true);
        IRuntimeCompiler compiler =
            machine.Program.RuntimeCompiler ?? throw new PrologException("Reading a term needs a compiler to parse it.");

        bool read = compiler.TryReadTerm(
            machine,
            source.Reader!,
            ref source.Buffer,
            out Cell value,
            out Cell names,
            out Cell variables,
            out Cell singletons
        );
        source.RecordInput(read);

        if (!read)
        {
            value = Cell.Atom(machine.Symbols.InternAtom("end_of_file"));
            names = Cell.Atom(machine.Symbols.EmptyList);
            variables = Cell.Atom(machine.Symbols.EmptyList);
            singletons = Cell.Atom(machine.Symbols.EmptyList);
        }

        return machine.Unify(machine.Argument(term), value)
            && (options < 0 || ApplyReadOptions(machine, options, names, variables, singletons));
    }

    /// <summary>
    /// Applies the ISO <c>read_term/2</c> options. Anything else is a <c>domain_error</c> rather
    /// than something quietly ignored.
    /// </summary>
    private static bool ApplyReadOptions(Machine machine, int options, Cell names, Cell variables, Cell singletons)
    {
        foreach (Cell element in TermList.ReadProper(machine, machine.Argument(options)))
        {
            Cell option = machine.Dereference(element);

            if (option.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (option.Tag != CellTag.Structure || machine.Symbols.ArityOf(machine.HeapAt(option.Index).Index) != 1)
            {
                throw PrologErrors.Domain(machine, "read_option", option);
            }

            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(option.Index).Index);
            Cell target = machine.HeapAt(option.Index + 1);

            bool applied = machine.Symbols.AtomName(functor.NameAtom) switch
            {
                "variable_names" => machine.Unify(target, names),
                "variables" => machine.Unify(target, variables),
                "singletons" => machine.Unify(target, singletons),
                _ => throw PrologErrors.Domain(machine, "read_option", option),
            };

            if (!applied)
            {
                return false;
            }
        }

        return true;
    }

    private static bool GetChar(Machine machine, int stream, int target, bool consume)
    {
        PrologStream source = Resolve(machine, stream, input: true);
        TextReader reader = source.Reader!;

        // Whatever a term read left behind has to be consumed before the reader itself is touched.
        if (source.Buffer.Length > 0)
        {
            char buffered = source.Buffer[0];
            if (consume)
            {
                source.Buffer = source.Buffer[1..];
                source.RecordInput(read: true);
            }

            return machine.Unify(machine.Argument(target), Character(machine, buffered));
        }

        int next = consume ? reader.Read() : reader.Peek();
        if (consume)
        {
            source.RecordInput(next >= 0);
        }

        return machine.Unify(
            machine.Argument(target),
            next < 0 ? Cell.Atom(machine.Symbols.InternAtom("end_of_file")) : Character(machine, (char)next)
        );
    }

    private static Cell Character(Machine machine, char value) => Cell.Atom(machine.Symbols.InternAtom(value.ToString()));

    private static bool GetCode(Machine machine, int stream, int target, bool consume)
    {
        PrologStream source = Resolve(machine, stream, input: true);
        Cell code = machine.Argument(target);

        if (code.Tag != CellTag.Reference)
        {
            if (code.Tag != CellTag.Integer)
            {
                throw PrologErrors.Type(machine, "integer", code);
            }

            if (code.Integer is < -1 or > char.MaxValue)
            {
                throw PrologErrors.Representation(machine, "in_character_code");
            }
        }

        int next;
        if (source.Buffer.Length > 0)
        {
            next = source.Buffer[0];
            if (consume)
            {
                source.Buffer = source.Buffer[1..];
                source.RecordInput(read: true);
            }
        }
        else
        {
            next = consume ? source.Reader!.Read() : source.Reader!.Peek();
        }

        if (consume)
        {
            source.RecordInput(next >= 0);
        }

        return machine.Unify(code, Cell.Integer60(next));
    }

    private static bool PutChar(Machine machine, int stream, int source)
    {
        PrologStream target = Resolve(machine, stream, input: false);
        Cell character = machine.Argument(source);

        if (character.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        string text =
            character.Tag == CellTag.Atom
                ? machine.Symbols.AtomName(character.Index)
                : throw PrologErrors.Type(machine, "character", character);

        if (text.Length != 1)
        {
            throw PrologErrors.Type(machine, "character", character);
        }

        target.Writer!.Write(text);
        return true;
    }

    private static bool PutCode(Machine machine, int stream, int source)
    {
        PrologStream target = Resolve(machine, stream, input: false);
        Cell code = machine.Argument(source);

        if (code.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (code.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", code);
        }

        if (code.Integer is < 0 or > char.MaxValue)
        {
            throw PrologErrors.Representation(machine, "character_code");
        }

        target.Writer!.Write((char)code.Integer);
        return true;
    }

    private static bool GetByte(Machine machine, int stream, int target, bool consume)
    {
        PrologStream source = Resolve(machine, stream, input: true, expectedType: "binary");
        Cell code = machine.Argument(target);

        if (code.Tag != CellTag.Reference && (code.Tag != CellTag.Integer || code.Integer is < -1 or > byte.MaxValue))
        {
            throw PrologErrors.Type(machine, "in_byte", code);
        }

        Stream input = source.BinaryStream!;
        int next;
        if (consume)
        {
            next = input.ReadByte();
            source.RecordInput(next >= 0);
        }
        else
        {
            long position = input.Position;
            next = input.ReadByte();
            input.Position = position;
        }

        return machine.Unify(code, Cell.Integer60(next));
    }

    private static bool PutByte(Machine machine, int stream, int source)
    {
        PrologStream target = Resolve(machine, stream, input: false, expectedType: "binary");
        Cell code = machine.Argument(source);

        if (code.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (code.Tag != CellTag.Integer || code.Integer is < 0 or > byte.MaxValue)
        {
            throw PrologErrors.Type(machine, "byte", code);
        }

        target.BinaryStream!.WriteByte((byte)code.Integer);
        return true;
    }

    private static bool AtEnd(Machine machine, int stream)
    {
        PrologStream source = Resolve(machine, stream, input: true, expectedType: null);
        return source.ObserveEnd() != PrologStream.EndState.Not;
    }

    private static bool SetStreamPosition(Machine machine)
    {
        Cell positionTerm = machine.Argument(1);
        if (positionTerm.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        PrologStream stream = ResolveEither(machine, 0);
        if (!TryReadPosition(machine, positionTerm, out long position))
        {
            throw PrologErrors.Domain(machine, "stream_position", positionTerm);
        }

        if (!stream.Reposition)
        {
            throw PrologErrors.Permission(
                machine,
                "reposition",
                "stream",
                machine.Symbols.InternFunctor(stream.Alias ?? stream.Name, 0)
            );
        }

        if (!stream.TrySetPosition(position))
        {
            throw PrologErrors.Domain(machine, "stream_position", positionTerm);
        }

        return true;
    }

    private static bool TryReadPosition(Machine machine, Cell term, out long position)
    {
        position = 0;
        if (term.Tag != CellTag.Structure)
        {
            return false;
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(term.Index).Index);
        if (machine.Symbols.AtomName(functor.NameAtom) != "$stream_position" || functor.Arity != 4)
        {
            return false;
        }

        for (int index = 1; index <= 4; index++)
        {
            Cell field = machine.Dereference(machine.HeapAt(term.Index + index));
            if (field.Tag != CellTag.Integer || field.Integer < 0)
            {
                return false;
            }

            if (index == 1)
            {
                position = field.Integer;
            }
        }

        return true;
    }

    private static bool WriteText(Machine machine, int stream, string text)
    {
        Resolve(machine, stream, input: false).Writer!.Write(text);
        return true;
    }

    private static bool WriteTerm(Machine machine, int stream, int term, bool quoted, bool ignoreOperators)
    {
        PrologStream target = Resolve(machine, stream, input: false);
        TermWriter.Write(machine, machine.Argument(term), target.Writer!, quoted, ignoreOperators);
        return true;
    }

    private static bool Flush(Machine machine, int stream)
    {
        PrologStream target = Resolve(machine, stream, input: false, expectedType: null);
        target.Writer?.Flush();
        target.BinaryStream?.Flush();
        return true;
    }

    private static bool ReadFromAtom(Machine machine)
    {
        Cell text = machine.Argument(0);

        if (text.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (text.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", text);
        }

        IRuntimeCompiler compiler =
            machine.Program.RuntimeCompiler ?? throw new PrologException("Reading a term needs a compiler to parse it.");

        using var reader = new StringReader(machine.Symbols.AtomName(text.Index));
        string buffer = string.Empty;
        bool read = compiler.TryReadTerm(
            machine,
            reader,
            ref buffer,
            out Cell value,
            out Cell names,
            out Cell variables,
            out Cell singletons
        );

        if (!read)
        {
            value = Cell.Atom(machine.Symbols.InternAtom("end_of_file"));
            names = Cell.Atom(machine.Symbols.EmptyList);
            variables = Cell.Atom(machine.Symbols.EmptyList);
            singletons = Cell.Atom(machine.Symbols.EmptyList);
        }

        return machine.Unify(machine.Argument(1), value) && ApplyReadOptions(machine, 2, names, variables, singletons);
    }

    private static bool CaptureBegin(Machine machine)
    {
        machine.Streams.BeginCapture();
        return true;
    }

    private static bool CaptureEnd(Machine machine) =>
        machine.Unify(machine.Argument(0), Cell.Atom(machine.Symbols.InternAtom(machine.Streams.EndCapture())));
}
