namespace Prolog.Runtime;

/// <summary>
/// The resume half of a nondeterministic native predicate: called on backtracking to produce the
/// next solution, carrying whatever state the previous one left behind.
/// </summary>
/// <param name="machine">The machine asking for another solution.</param>
/// <param name="state">
/// The value passed to <see cref="Machine.PushRetry(long)"/> last time. Pass a new one to
/// <see cref="Machine.PushRetry(long)"/> again to offer a further solution after this one.
/// </param>
/// <returns><see langword="true"/> if another solution was produced.</returns>
public delegate bool PrologRetry(Machine machine, long state);
