namespace DotProlog.Syntax;

/// <summary>A lexical word completion site in an immutable source snapshot.</summary>
/// <param name="Text">The identifier prefix before the cursor.</param>
/// <param name="Range">The full identifier to replace, in UTF-16 offsets.</param>
/// <param name="IsVariable">Whether the identifier is a variable.</param>
/// <param name="Variables">Other named variables in the same lexical clause.</param>
/// <param name="Module">An explicit atom module qualifier, or null for unqualified completion.</param>
public sealed record PrologCompletionPrefix(
    string Text,
    SourceSpan Range,
    bool IsVariable,
    IReadOnlyList<string> Variables,
    string? Module
);
