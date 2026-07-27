namespace DotProlog.Runtime;

/// <summary>
/// A deterministic native predicate. Arguments are read from the machine's argument registers with
/// <see cref="Machine.Argument"/>; the return value reports success or failure, never an exception.
/// </summary>
/// <param name="machine">The machine invoking the predicate.</param>
/// <returns><see langword="true"/> if the goal succeeds.</returns>
public delegate bool PrologBuiltin(Machine machine);
