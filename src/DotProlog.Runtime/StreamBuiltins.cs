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
/// There are no binary streams and no repositioning. A stream is a source of terms and characters,
/// which is what a Prolog program reads; a program that needs bytes should be given them by its host.
/// </para>
/// </remarks>
internal static class StreamBuiltins
{
    private const string StreamFunctor = "$stream";

    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("open", 3, static machine => Open(machine, options: -1));
        registry.Register("open", 4, static machine => Open(machine, options: 3));
        registry.Register("close", 1, Close);
        registry.Register("current_input", 1, static machine => Current(machine, input: true));
        registry.Register("current_output", 1, static machine => Current(machine, input: false));
        registry.Register("set_input", 1, static machine => Set(machine, input: true));
        registry.Register("set_output", 1, static machine => Set(machine, input: false));

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

        registry.Register("at_end_of_stream", 0, static machine => AtEnd(machine, stream: -1));
        registry.Register("at_end_of_stream", 1, static machine => AtEnd(machine, stream: 0));

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
    private static PrologStream Resolve(Machine machine, int index, bool input)
    {
        if (index < 0)
        {
            return input ? machine.Streams.CurrentInput : machine.Streams.CurrentOutput;
        }

        Cell cell = machine.Argument(index);

        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        PrologStream? stream;

        if (cell.Tag == CellTag.Atom)
        {
            stream = machine.Streams.ByAlias(machine.Symbols.AtomName(cell.Index));
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

            stream = machine.Streams.ById((int)id.Integer);
        }
        else
        {
            throw PrologErrors.Domain(machine, "stream_or_alias", cell);
        }

        if (stream is null)
        {
            throw Existence(machine, "stream", cell);
        }

        if (input && stream.Reader is null)
        {
            throw PrologErrors.Permission(machine, "input", "stream", machine.Symbols.InternFunctor(stream.Name, 0));
        }

        if (!input && stream.Writer is null)
        {
            throw PrologErrors.Permission(machine, "output", "stream", machine.Symbols.InternFunctor(stream.Name, 0));
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

        string? alias = options < 0 ? null : ReadAlias(machine, options);
        string path = machine.Symbols.AtomName(file.Index);

        PrologStream stream;
        try
        {
            stream = machine.Streams.Open(path, modeName, alias);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Existence(machine, "source_sink", file);
        }

        return machine.Unify(machine.Argument(2), StreamTerm(machine, stream));
    }

    /// <summary>Reads the <c>alias/1</c> option of <c>open/4</c>; the rest are rejected rather than ignored.</summary>
    private static string? ReadAlias(Machine machine, int options)
    {
        string? alias = null;

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

            if (machine.Symbols.AtomName(functor.NameAtom) != "alias" || value.Tag != CellTag.Atom)
            {
                throw PrologErrors.Domain(machine, "stream_option", option);
            }

            alias = machine.Symbols.AtomName(value.Index);
        }

        return alias;
    }

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
            CellTag.Structure => ById(machine, cell),
            _ => null,
        };

        return stream ?? throw Existence(machine, "stream", cell);
    }

    private static PrologStream? ById(Machine machine, Cell cell)
    {
        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);
        Cell id = machine.Dereference(machine.HeapAt(cell.Index + 1));

        return machine.Symbols.AtomName(functor.NameAtom) == StreamFunctor && functor.Arity == 1 && id.Tag == CellTag.Integer
            ? machine.Streams.ById((int)id.Integer)
            : null;
    }

    private static bool Current(Machine machine, bool input)
    {
        PrologStream stream = input ? machine.Streams.CurrentInput : machine.Streams.CurrentOutput;
        return machine.Unify(machine.Argument(0), StreamTerm(machine, stream));
    }

    private static bool Set(Machine machine, bool input)
    {
        PrologStream stream = Resolve(machine, 0, input);

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
            }

            return machine.Unify(machine.Argument(target), Character(machine, buffered));
        }

        int next = consume ? reader.Read() : reader.Peek();

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
            }
        }
        else
        {
            next = consume ? source.Reader!.Read() : source.Reader!.Peek();
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

    private static bool AtEnd(Machine machine, int stream)
    {
        PrologStream source = Resolve(machine, stream, input: true);
        return source.Buffer.Length == 0 && source.Reader!.Peek() < 0;
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
        Resolve(machine, stream, input: false).Writer!.Flush();
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
