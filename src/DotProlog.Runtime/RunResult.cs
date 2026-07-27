namespace DotProlog.Runtime;

/// <summary>How a call to <see cref="Machine.Run"/> ended.</summary>
public enum RunResult
{
    /// <summary>The goal was proved.</summary>
    Success,

    /// <summary>The goal failed and no choice points remained.</summary>
    Failure,

    /// <summary>The goal executed <c>halt/0</c> or <c>halt/1</c>.</summary>
    Halted,
}
