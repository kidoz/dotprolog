using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Identifies source conditions that prevent complete static call analysis.</summary>
internal static class PrologLintCompleteness
{
    internal static bool HasProgramWideGaps(PrologLintProgramModel model) =>
        model.HasUnresolvedImports
        || model.Sources.Any(source => source.HasAnalysisGaps)
        || model.Clauses.Any(DefinesSourceExpansion)
        || model.Sources.SelectMany(source => source.Clauses).Any(HasUnrecognizedDirective)
        || model.Sources.SelectMany(source => source.Clauses).Any(HasQualifiedClauseHead);

    private static bool HasQualifiedClauseHead(SyntaxTerm clause) =>
        LintSyntax.ClauseHead(clause) is CompoundTerm { Name: ":", Arity: 2 };

    private static bool DefinesSourceExpansion(PrologLintProgramModel.ClauseContext clause) =>
        clause.Indicator is { Arity: 2, Name: "term_expansion" or "goal_expansion" };

    private static bool HasUnrecognizedDirective(SyntaxTerm clause) =>
        clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [SyntaxTerm directive] } && !IsDeclarativeDirective(directive);

    private static bool IsDeclarativeDirective(SyntaxTerm directive) =>
        directive is CompoundTerm compound
        && (compound.Name, compound.Arity) switch
        {
            ("module", 1 or 2) => true,
            ("export", 1) => true,
            ("use_module", 1 or 2) => true,
            ("import", 1 or 2) => true,
            ("reexport", 1 or 2) => true,
            ("dynamic", 1) => true,
            ("multifile", 1) => true,
            ("discontiguous", 1) => true,
            ("meta_predicate", 1) => true,
            ("metapredicate", 1) => true,
            ("end_module", 1) => true,
            ("body", 1) => true,
            ("end_body", 1) => true,
            ("op", 3) => true,
            ("set_prolog_flag", 2) => true,
            ("char_conversion", 2) => true,
            ("include", 1) => true,
            ("initialization", 1 or 2) => true,
            _ => false,
        };
}
