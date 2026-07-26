namespace Prolog.CodeGen.CSharp;

/// <summary>Whether an exported predicate's argument is passed in or read back out.</summary>
public enum ArgumentMode
{
    /// <summary>A value the caller supplies; becomes a method parameter.</summary>
    In,

    /// <summary>A value the predicate produces; becomes part of the result.</summary>
    Out,
}
