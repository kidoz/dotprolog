namespace DotProlog.Compiler;

/// <summary>Stable diagnostic identifiers produced by source linting.</summary>
public static class LintDiagnosticIds
{
    /// <summary>An ordinary named variable occurs only once in its clause.</summary>
    public const string SingletonVariable = "DPL3001";

    /// <summary>An underscore-prefixed singleton marker occurs more than once in its clause.</summary>
    public const string RepeatedSingletonMarker = "DPL3002";

    /// <summary>A tab character appears in source governed by a spaces-only profile.</summary>
    public const string TabCharacter = "DPL3003";

    /// <summary>A clause continuation uses an inconsistent indentation width.</summary>
    public const string InconsistentIndentation = "DPL3004";

    /// <summary>A source line exceeds the configured length.</summary>
    public const string LineTooLong = "DPL3005";

    /// <summary>A clause spans more than the configured number of lines.</summary>
    public const string ClauseTooLong = "DPL3006";

    /// <summary>A comma outside quoted text or a comment is not followed by layout.</summary>
    public const string MissingSpaceAfterComma = "DPL3007";

    /// <summary>A clause or rule body does not begin at the configured line boundary.</summary>
    public const string ClauseLayout = "DPL3008";

    /// <summary>Two conjunction subgoals begin on the same source line.</summary>
    public const string SubgoalLayout = "DPL3009";

    /// <summary>A source line ends in spaces or tabs.</summary>
    public const string TrailingWhitespace = "DPL3010";

    /// <summary>
    /// Double-quoted text reads, under the <c>double_quotes</c> flag in force, as a different term
    /// than the conversion it is passed to expects — characters where codes are needed, or the
    /// reverse.
    /// </summary>
    public const string DoubleQuotedListKindMismatch = "DPL3011";

    /// <summary>
    /// Double-quoted text reads as a list of characters, but the grammar in the same file that parses
    /// it compares character codes.
    /// </summary>
    public const string CodeGrammarOverCharacters = "DPL3012";

    /// <summary>A literal format string, argument count, or argument type is invalid.</summary>
    public const string InvalidFormatCall = "DPL3013";

    /// <summary>A literal predicate option or option value is invalid.</summary>
    public const string InvalidPredicateOption = "DPL3014";

    /// <summary>An arithmetic expression contains a statically invalid function or operand.</summary>
    public const string InvalidArithmeticExpression = "DPL3015";

    /// <summary>A call has no matching built-in or file-local predicate signature.</summary>
    public const string PossibleBuiltinArityMismatch = "DPL3016";

    /// <summary>A goal succeeds without changing bindings or producing another observable effect.</summary>
    public const string NoEffectGoal = "DPL3017";

    /// <summary>A goal is statically determined to fail whenever execution reaches it.</summary>
    public const string AlwaysFalseGoal = "DPL3018";

    /// <summary>An existentially qualified variable does not occur in the qualified goal.</summary>
    public const string UnusedExistentialVariable = "DPL3019";

    /// <summary>An all-solutions template and goal share no named variables.</summary>
    public const string UnsharedAllSolutionsTemplate = "DPL3020";

    /// <summary>A variable occurs only once inside a bagof or setof goal.</summary>
    public const string AllSolutionsSingleton = "DPL3021";

    /// <summary>A findall call is followed directly by failure and is used only for backtracking.</summary>
    public const string FindallBacktracking = "DPL3022";

    /// <summary>Clauses of one predicate are separated without a discontiguous declaration.</summary>
    public const string DiscontiguousPredicate = "DPL3023";

    /// <summary>A clause or grammar rule duplicates an earlier one up to variable renaming.</summary>
    public const string DuplicateClause = "DPL3024";

    /// <summary>A predicate declaration duplicates an earlier declaration.</summary>
    public const string DuplicateDirective = "DPL3025";

    /// <summary>A clause or grammar rule starts with a variant of its own head.</summary>
    public const string DirectLeftRecursion = "DPL3026";

    /// <summary>A discontiguous or meta-predicate declaration has no file-local definition.</summary>
    public const string DeclaredButUndefined = "DPL3027";

    /// <summary>A variable occurs once in each alternative branch and nowhere outside them.</summary>
    public const string SemanticSingletonVariable = "DPL3028";

    /// <summary>A statically resolved call has no definition in the supplied program.</summary>
    public const string UndefinedPredicate = "DPL3029";

    /// <summary>An exported or declared predicate has no definition in the supplied program.</summary>
    public const string MissingPredicateDefinition = "DPL3030";

    /// <summary>A predicate is called as a nonterminal, or a nonterminal as a predicate.</summary>
    public const string PredicateNonterminalMismatch = "DPL3031";

    /// <summary>An include cannot be read or exceeds the non-executing expansion limits.</summary>
    public const string UnresolvedLintSource = "DPL3032";

    /// <summary>No supplied clause head can unify with a statically resolved ground call.</summary>
    public const string TrivialCallFailure = "DPL3033";

    /// <summary>No recognized entry point reaches an ordinary predicate through static calls.</summary>
    public const string PossibleUnreferencedPredicate = "DPL3034";

    /// <summary>An imported predicate or nonterminal has no static reference in its importing module.</summary>
    public const string PossiblyUnusedImport = "DPL3035";
}
