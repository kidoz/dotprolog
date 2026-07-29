namespace DotProlog.Runtime;

/// <summary>
/// The compiler seen from inside a running program, so that <c>assertz/1</c> and <c>consult/1</c> can
/// turn terms and files into bytecode without the runtime referencing the compiler assembly.
/// </summary>
/// <remarks>
/// This is the seam that keeps the runtime-consult path valid under NativeAOT. The implementation
/// produces bytecode for the existing virtual machine; it never emits CLR IL, loads an assembly, or
/// calls Roslyn, so nothing here needs a JIT at run time.
/// </remarks>
public interface IRuntimeCompiler
{
    /// <summary>
    /// Compiles a control term reached through <c>call/1</c> as an anonymous bytecode clause.
    /// </summary>
    /// <param name="machine">Machine owning the heap the term and its variables live on.</param>
    /// <param name="goal">The callable control term.</param>
    /// <param name="arguments">
    /// Destination for the original heap variables that become arguments of the anonymous clause.
    /// </param>
    /// <param name="argumentCount">Number of cells written to <paramref name="arguments"/>.</param>
    /// <returns>The bytecode address of the anonymous clause.</returns>
    int CompileControlGoal(Machine machine, Cell goal, Span<Cell> arguments, out int argumentCount);

    /// <summary>
    /// Compiles a clause term into the program and returns the address of its code.
    /// </summary>
    /// <param name="machine">Machine owning the heap the term lives on.</param>
    /// <param name="clause">A <c>Head :- Body</c> term, or a bare head.</param>
    /// <param name="functorId">The head's functor identifier.</param>
    /// <exception cref="PrologException">The term is not a valid clause.</exception>
    int CompileClause(Machine machine, Cell clause, out int functorId);

    /// <summary>Reads a Prolog source file and loads its clauses into the running program.</summary>
    /// <param name="machine">The machine to load into.</param>
    /// <param name="path">Path of the file to read.</param>
    /// <exception cref="PrologException">The file cannot be read, or does not compile.</exception>
    void ConsultFile(Machine machine, string path);

    /// <summary>
    /// Reads one term from <paramref name="input"/> and builds it on the heap.
    /// </summary>
    /// <param name="machine">Machine owning the heap the term is built on.</param>
    /// <param name="input">Where to read characters from.</param>
    /// <param name="buffer">
    /// Text read past the end of the previous clause. The caller owns this — it belongs to the
    /// stream — and the implementation replaces it with whatever is left over this time.
    /// </param>
    /// <param name="term">The term read.</param>
    /// <param name="variableNames">A list of <c>Name=Variable</c> for the term's named variables.</param>
    /// <param name="variables">Every distinct variable in first-occurrence order.</param>
    /// <param name="singletons">
    /// A list of <c>Name=Variable</c> for named variables that occur exactly once.
    /// </param>
    /// <returns><see langword="false"/> at end of input, where the caller reports <c>end_of_file</c>.</returns>
    /// <exception cref="PrologException">The text read does not parse.</exception>
    bool TryReadTerm(
        Machine machine,
        TextReader input,
        ref string buffer,
        out Cell term,
        out Cell variableNames,
        out Cell variables,
        out Cell singletons
    );
}
