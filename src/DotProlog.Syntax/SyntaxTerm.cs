namespace DotProlog.Syntax;

/// <summary>
/// A term as written in source. This is the reader's output: it keeps source spans and variable
/// names, and is lowered to semantic IR and then to bytecode or C# by the compiler.
/// </summary>
/// <param name="Span">Source range the term was read from.</param>
public abstract record SyntaxTerm(SourceSpan Span);
