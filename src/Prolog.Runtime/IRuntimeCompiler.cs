namespace Prolog.Runtime;

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
}
