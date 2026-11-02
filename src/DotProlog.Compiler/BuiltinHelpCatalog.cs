using DotProlog.Runtime;

namespace DotProlog.Compiler;

// Signatures remain governed by the actual registrations/libraries. These descriptions do not add predicates.
internal static class BuiltinHelpCatalog
{
    internal static bool IsControl(PredicateIndicator predicate) =>
        (predicate.Name, predicate.Arity) is ("," or ";" or "->", 2) or ("\\+" or "call", 1) or ("catch", 3);

    internal static PrologBuiltinDocumentation? Find(PredicateIndicator predicate, PrologLanguageMode mode)
    {
        if (predicate.Name.StartsWith('$') || (!IsControl(predicate) && !LintBuiltinCatalog.Contains(predicate)))
            return null;
        return new PrologBuiltinDocumentation(
            LintSyntax.Display(predicate),
            predicate is { Name: "sort", Arity: 4 }
                ? "Sorts by a selected argument and order; the order controls direction and duplicate retention."
                : Description(predicate.Name),
            mode == PrologLanguageMode.StrictIso
            && !IsControl(predicate)
            && !IsoLanguageProfile.IsStandardPredicate(predicate.Name, predicate.Arity)
                ? "Modern extension; unavailable as a predefined predicate in StrictIso."
                : null
        );
    }

    private static string Description(string name) =>
        name switch
        {
            "," => "Runs the left goal, then the right goal for each solution.",
            ";" => "Tries alternative goals; also separates the branches of if-then-else.",
            "->" => "Commits to the first solution of the condition and runs the then-goal.",
            "\\+" or "not" => "Succeeds when its goal has no solution; bindings made by the goal are undone.",
            "!" => "Discards alternatives created since entry to the current predicate, within its cut scope.",
            "true" => "Succeeds once without changing bindings.",
            "fail" or "false" => "Always fails.",
            "repeat" => "Succeeds and offers another success on each backtracking step.",
            "call" => "Calls a goal, appending any additional arguments to the callable term.",
            "once" => "Keeps only the first solution of its goal.",
            "ignore" => "Tries its goal once and succeeds even if that goal fails.",
            "catch" => "Runs a goal and calls the recovery goal when a thrown term unifies with the catcher.",
            "throw" => "Raises a Prolog exception carrying the supplied term.",
            "halt" => "Requests termination, optionally with an integer exit code.",
            "=" => "Unifies two terms, binding variables as needed.",
            "\\=" => "Succeeds when the two terms cannot unify; leaves their bindings unchanged.",
            "unify_with_occurs_check" => "Unifies two terms without creating a cyclic binding.",
            "==" => "Tests whether two terms are identical without binding variables.",
            "\\==" => "Tests whether two terms are not identical without binding variables.",
            "@<" or "@=<" or "@>" or "@>=" => "Compares two terms using the standard order of terms.",
            "compare" => "Relates an ordering atom (<, = or >) to the standard-order comparison of two terms.",
            "subsumes_term" => "Tests whether the general term can match the specific term without retaining bindings.",
            "is" => "Evaluates the arithmetic expression on the right and unifies its numeric value with the left.",
            "=:=" or "=\\=" or "<" or "=<" or ">" or ">=" =>
                "Evaluates two arithmetic expressions and compares their numeric values.",
            "var" => "Tests whether a term is an unbound variable.",
            "nonvar" => "Tests whether a term is not an unbound variable.",
            "atom" => "Tests whether a term is an atom.",
            "integer" => "Tests whether a term is an integer.",
            "float" => "Tests whether a term is a floating-point number.",
            "number" => "Tests whether a term is a number.",
            "rational" => "Tests whether a term is an exact rational number or integer.",
            "atomic" => "Tests whether a term is atomic rather than a variable or compound term.",
            "compound" => "Tests whether a term is a compound term.",
            "callable" => "Tests whether a term has a callable shape: an atom or compound term.",
            "string" => "Tests whether a term is a native string value.",
            "ground" => "Tests whether a term contains no unbound variables.",
            "acyclic_term" => "Tests whether a term contains no cycles.",
            "is_list" => "Tests whether a term is a proper list.",
            "functor" => "Relates a term to its principal functor name and arity.",
            "arg" => "Selects an argument of a compound term by its one-based index.",
            "=.." => "Relates a term to a list containing its functor followed by its arguments.",
            "copy_term" => "Copies a term with fresh variables while preserving variable sharing.",
            "term_variables" => "Collects the distinct variables of a term in traversal order.",
            "term_size" => "Relates a term to the size of its copied representation.",
            "setarg" => "Changes a compound argument, with the change undone on backtracking.",
            "nb_setarg" => "Changes a compound argument without undoing the change on backtracking.",
            "member" => "Relates an element to a list member, enumerating alternatives on backtracking.",
            "memberchk" => "Tests membership and commits to the first matching element.",
            "append" => "Relates lists to their concatenation.",
            "length" => "Relates a proper list to its number of elements.",
            "reverse" => "Relates a list to the same elements in reverse order.",
            "select" => "Relates a list to an element removed from it and the remaining list.",
            "nth0" => "Selects a list element using a zero-based index.",
            "nth1" => "Selects a list element using a one-based index.",
            "last" => "Relates a nonempty list to its final element.",
            "flatten" => "Flattens nested list structure into one list.",
            "sort" => "Sorts terms in standard order and removes duplicates.",
            "msort" => "Sorts terms in standard order while retaining duplicates.",
            "keysort" => "Stably sorts key-value pairs by key, retaining duplicates.",
            "predsort" => "Sorts using a supplied comparison predicate.",
            "maplist" => "Applies a callable to corresponding elements of one or more lists.",
            "include" => "Keeps list elements for which the supplied callable succeeds.",
            "exclude" => "Keeps list elements for which the supplied callable fails.",
            "partition" => "Separates list elements according to a supplied callable.",
            "foldl" => "Threads an accumulator through calls over corresponding list elements.",
            "findall" => "Collects copies of a template for every solution of a goal; returns an empty list when none exist.",
            "bagof" => "Collects template instances, grouped by the goal's free variables; fails when no solution exists.",
            "setof" => "Collects sorted, distinct template instances, grouped by free variables; fails when no solution exists.",
            "forall" => "Checks that the action succeeds for every solution of the condition, without retaining bindings.",
            "aggregate" or "aggregate_all" => "Computes an aggregate over the solutions of a goal.",
            "countall" => "Counts the solutions of a goal.",
            "call_nth" => "Relates each solution of a goal to its one-based solution number.",
            "phrase" => "Runs a grammar body against an input list, optionally exposing the unconsumed remainder.",
            "between" => "Enumerates integers within inclusive lower and upper bounds.",
            "succ" => "Relates a nonnegative integer to its successor.",
            "plus" => "Relates two integers to their sum.",
            "assert" or "assertz" => "Adds a clause at the end of a dynamic predicate.",
            "asserta" => "Adds a clause at the beginning of a dynamic predicate.",
            "retract" => "Removes matching dynamic clauses, offering matches on backtracking.",
            "retractall" => "Removes all dynamic clauses whose heads match the supplied pattern.",
            "abolish" => "Removes a dynamic predicate's clauses and associated properties.",
            "clause" => "Inspects a dynamic predicate's clauses as head and body terms.",
            "current_predicate" => "Enumerates visible predicate indicators.",
            "predicate_property" => "Queries properties of a visible predicate.",
            "current_module" => "Enumerates known modules.",
            "consult" => "Loads and compiles Prolog source into the running program.",
            "ensure_loaded" => "Loads Prolog source unless it has already been loaded.",
            "op" => "Defines or removes an operator declaration.",
            "current_op" => "Enumerates operator priorities, specifiers and names.",
            "set_prolog_flag" => "Changes a writable Prolog flag.",
            "current_prolog_flag" => "Queries the value of a Prolog flag.",
            "char_conversion" => "Defines a character conversion used when conversion is enabled.",
            "current_char_conversion" => "Enumerates character conversions.",
            "atom_length" => "Relates an atom to its length under the active character model.",
            "atom_chars" => "Relates an atom to a list of its characters.",
            "atom_codes" => "Relates an atom to a list of its character codes.",
            "char_code" => "Relates a character atom to its numeric code.",
            "atom_concat" => "Relates two atoms to their concatenation.",
            "sub_atom" => "Relates an atom to a substring and its before, length and after counts.",
            "number_chars" or "number_codes" => "Relates a number to its textual character or code representation.",
            "atom_number" => "Converts between an atom and a number.",
            "atomic_list_concat" => "Joins atomic list elements into an atom, optionally using a separator.",
            "upcase_atom" or "downcase_atom" => "Converts the letter case of an atom.",
            "char_type" or "code_type" => "Classifies a character or character code.",
            "atom_string" or "string_to_atom" => "Relates atom and string representations of text.",
            "text_to_string" => "Converts supported text representations to a native string.",
            "string_length" => "Relates a string to its length under the active character model.",
            "string_chars" or "string_codes" => "Relates a string to its character or character-code list.",
            "number_string" => "Relates a number to its string representation.",
            "split_string" => "Splits text at separator characters with padding removal.",
            "string_lower" or "string_upper" => "Converts the letter case of string text.",
            "open" => "Opens a source or sink as a stream, with an optional list of stream options.",
            "close" => "Closes a stream, optionally using close options.",
            "current_input" or "current_output" => "Returns the current input or output stream.",
            "set_input" or "set_output" => "Changes the current input or output stream.",
            "current_stream" or "stream_property" => "Queries open streams and their properties.",
            "set_stream_position" => "Restores a stream to a previously obtained position.",
            "at_end_of_stream" => "Tests whether an input stream is at its end.",
            "flush_output" => "Flushes buffered output on the selected or current output stream.",
            "get_char" or "get_code" or "get_byte" =>
                "Reads a character, code or byte from the selected or current input stream.",
            "peek_char" or "peek_code" or "peek_byte" => "Examines the next character, code or byte without consuming it.",
            "put_char" or "put_code" or "put_byte" =>
                "Writes a character, code or byte to the selected or current output stream.",
            "read" or "read_term" =>
                "Reads a Prolog term from the selected or current input stream, optionally using reader options.",
            "write" or "writeq" or "write_canonical" or "write_term" or "print" =>
                "Writes a Prolog term using the predicate's quoting, operator and option conventions.",
            "writeln" => "Writes a term followed by a newline.",
            "nl" => "Writes a newline to the selected or current output stream.",
            "tab" => "Writes a specified number of spaces.",
            "format" => "Writes formatted output according to a control text and argument list.",
            "with_output_to" => "Runs a goal with output redirected to a destination.",
            "freeze" => "Delays a goal until its variable is bound.",
            "frozen" => "Returns goals delayed on a variable.",
            "b_setval" => "Stores a global value with updates undone on backtracking.",
            "nb_setval" => "Stores a global value whose update survives backtracking.",
            "b_getval" or "nb_getval" or "nb_current" => "Retrieves a stored global value.",
            "call_cleanup" or "setup_call_cleanup" =>
                "Arranges cleanup around a goal; use once/1 when committing to its first solution.",
            ":" => "Calls a goal in an explicitly selected module context.",
            "selectchk" => "Removes the first matching list element and commits to that choice.",
            "delete" => "Removes matching elements from a list.",
            "permutation" => "Relates lists containing the same elements in different orders.",
            "numlist" => "Builds the list of consecutive integers between inclusive bounds.",
            "sumlist" or "sum_list" => "Sums the numbers in a list.",
            "min_list" or "max_list" => "Finds the numeric minimum or maximum of a nonempty list.",
            "min_member" or "max_member" => "Finds the minimum or maximum list member in standard term order.",
            "list_to_set" => "Removes duplicate elements while retaining the first occurrences.",
            "list_to_ord_set" => "Sorts a list into a duplicate-free ordered set.",
            "intersection" or "union" or "subtract" => "Computes a list-based set intersection, union or difference.",
            "ord_empty" => "Tests or constructs the empty ordered set.",
            "ord_memberchk" => "Tests membership in an ordered set.",
            "ord_add_element" or "ord_del_element" => "Adds or removes an element while preserving ordered-set form.",
            "ord_disjoint" => "Tests whether two ordered sets have no common elements.",
            "ord_seteq" => "Tests whether two ordered sets are identical.",
            "ord_subset" => "Tests whether every element of the first ordered set belongs to the second.",
            "ord_intersection" or "ord_union" or "ord_subtract" or "ord_symdiff" =>
                "Combines ordered sets using intersection, union, difference or symmetric difference.",
            "empty_assoc" => "Constructs or tests an empty association map.",
            "is_assoc" => "Tests whether a term is a valid association map.",
            "get_assoc" => "Looks up a key in an association map, optionally replacing its value.",
            "put_assoc" => "Inserts or replaces a key-value entry in an association map.",
            "del_assoc" => "Removes a key from an association map and returns its value.",
            "gen_assoc" => "Enumerates key-value entries of an association map.",
            "min_assoc" or "max_assoc" => "Returns the entry with the least or greatest key in an association map.",
            "del_min_assoc" or "del_max_assoc" => "Removes the entry with the least or greatest key from an association map.",
            "assoc_to_list" or "assoc_to_keys" or "assoc_to_values" =>
                "Extracts entries, keys or values from an association map in key order.",
            "list_to_assoc" or "ord_list_to_assoc" => "Builds an association map from key-value pairs.",
            "map_assoc" => "Applies a callable to the values of an association map.",
            "pairs_keys_values" or "pairs_keys" or "pairs_values" => "Relates key-value pairs to their keys and values.",
            "transpose_pairs" => "Swaps the keys and values of pairs and sorts by the new keys.",
            "variant" => "Tests whether terms differ only in consistent renaming of variables.",
            "numbervars" => "Replaces variables with numbered placeholders, preserving shared occurrences.",
            "must_be" => "Checks a type condition and throws an error when it is not met.",
            "is_of_type" => "Tests whether a term satisfies a supported type condition.",
            "instantiation_error"
            or "uninstantiation_error"
            or "type_error"
            or "domain_error"
            or "existence_error"
            or "permission_error"
            or "representation_error"
            or "resource_error"
            or "syntax_error" => "Raises a Prolog error of the indicated category.",
            "atom_to_term" or "read_term_from_atom" => "Reads a Prolog term from atom text.",
            "term_to_atom" or "term_string" => "Relates a Prolog term to its textual representation.",
            "portray_clause" => "Writes a clause in readable Prolog source form.",
            "print_message" => "Prints a diagnostic message for the supplied severity and term.",
            "initialization" => "Runs an initialization goal at a supported initialization phase.",
            "string_concat" => "Relates two strings to their concatenation.",
            "sub_string" => "Relates a string to a substring and its before, length and after counts.",
            "string_code" => "Relates a position in string text to its character code.",
            _ => "A predefined predicate supplied by DotProlog's runtime or bundled Prolog libraries.",
        };
}
