using System.ComponentModel;

namespace DotProlog.Runtime;

/// <summary>A statically generated block in a build-time-compiled Prolog predicate.</summary>
/// <param name="execution">The explicit machine state shared with runtime bytecode.</param>
/// <param name="program">The generated compilation unit's resolved symbols and constants.</param>
/// <returns><see langword="true"/> to continue, or <see langword="false"/> to backtrack.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate bool CompiledPredicateBlock(ref Machine.CompiledExecution execution, CompiledProgram program);
