using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// Interns the atoms, functors, floats, and big integers a program refers to, so that cells can
/// carry small integer identifiers instead of object references. Identifiers are dense and
/// allocated in order, which lets the runtime index tables by identifier rather than hashing.
/// </summary>
public sealed class SymbolTable
{
    private readonly Dictionary<string, int> _atomIds = new(StringComparer.Ordinal);
    private readonly List<string> _atomNames = [];
    private readonly Dictionary<Functor, int> _functorIds = [];
    private readonly List<Functor> _functors = [];
    private readonly Dictionary<double, int> _floatIds = [];
    private readonly List<double> _floats = [];
    private readonly Dictionary<BigInteger, int> _bigIds = [];
    private readonly List<BigInteger> _bigs = [];

    /// <summary>Creates a table pre-populated with the atoms the runtime itself refers to.</summary>
    public SymbolTable()
    {
        EmptyList = InternAtom("[]");
        ListName = InternAtom(".");
        True = InternAtom("true");
        Fail = InternAtom("fail");
        Curly = InternAtom("{}");
        ListFunctor = InternFunctor(ListName, 2);
    }

    /// <summary>Identifier of the empty list atom <c>[]</c>.</summary>
    public int EmptyList { get; }

    /// <summary>Identifier of the list constructor name <c>'.'</c>.</summary>
    public int ListName { get; }

    /// <summary>Identifier of the atom <c>true</c>.</summary>
    public int True { get; }

    /// <summary>Identifier of the atom <c>fail</c>.</summary>
    public int Fail { get; }

    /// <summary>Identifier of the atom <c>{}</c>.</summary>
    public int Curly { get; }

    /// <summary>Identifier of the list constructor <c>'.'/2</c>.</summary>
    public int ListFunctor { get; }

    /// <summary>Number of distinct functors interned so far.</summary>
    public int FunctorCount => _functors.Count;

    /// <summary>Returns the identifier for <paramref name="name"/>, interning it if necessary.</summary>
    public int InternAtom(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_atomIds.TryGetValue(name, out var id))
        {
            return id;
        }

        id = _atomNames.Count;
        _atomNames.Add(name);
        _atomIds[name] = id;
        return id;
    }

    /// <summary>Returns the text of atom <paramref name="atomId"/>.</summary>
    public string AtomName(int atomId) => _atomNames[atomId];

    /// <summary>Returns the identifier for a functor, interning it if necessary.</summary>
    public int InternFunctor(int nameAtom, int arity)
    {
        var functor = new Functor(nameAtom, arity);
        if (_functorIds.TryGetValue(functor, out var id))
        {
            return id;
        }

        id = _functors.Count;
        _functors.Add(functor);
        _functorIds[functor] = id;
        return id;
    }

    /// <summary>Returns the identifier for a functor named <paramref name="name"/>, interning it if necessary.</summary>
    public int InternFunctor(string name, int arity) => InternFunctor(InternAtom(name), arity);

    /// <summary>Returns the functor with identifier <paramref name="functorId"/>.</summary>
    public Functor GetFunctor(int functorId) => _functors[functorId];

    /// <summary>Returns the arity of functor <paramref name="functorId"/>.</summary>
    public int ArityOf(int functorId) => _functors[functorId].Arity;

    /// <summary>Returns a display form of a functor, such as <c>append/3</c>.</summary>
    public string DescribeFunctor(int functorId)
    {
        Functor functor = _functors[functorId];
        return $"{_atomNames[functor.NameAtom]}/{functor.Arity}";
    }

    /// <summary>
    /// Returns the identifier for <paramref name="value"/>, interning it if necessary. Interning by
    /// value keeps cell equality usable as float identity.
    /// </summary>
    public int InternFloat(double value)
    {
        if (_floatIds.TryGetValue(value, out var id))
        {
            return id;
        }

        id = _floats.Count;
        _floats.Add(value);
        _floatIds[value] = id;
        return id;
    }

    /// <summary>Returns the float with identifier <paramref name="floatId"/>.</summary>
    public double GetFloat(int floatId) => _floats[floatId];

    /// <summary>
    /// Returns the identifier for <paramref name="value"/>, interning it if necessary. The value
    /// must be outside the fixnum range — values that fit a cell stay <see cref="CellTag.Integer"/>
    /// cells, which keeps cell equality usable as integer value identity.
    /// </summary>
    public int InternBig(BigInteger value)
    {
        if (_bigIds.TryGetValue(value, out var id))
        {
            return id;
        }

        id = _bigs.Count;
        _bigs.Add(value);
        _bigIds[value] = id;
        return id;
    }

    /// <summary>Returns the big integer with identifier <paramref name="bigId"/>.</summary>
    public BigInteger GetBig(int bigId) => _bigs[bigId];
}
