namespace Prolog.CodeGen.CSharp;

/// <summary>One argument of an exported predicate.</summary>
/// <param name="Mode">Whether the argument is passed in or read out.</param>
/// <param name="Name">The argument's name, used for the C# parameter or result member.</param>
/// <param name="Type">The argument's term shape.</param>
public sealed record ContractArgument(ArgumentMode Mode, string Name, ContractType Type);
