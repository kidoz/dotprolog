namespace DotProlog.Runtime;

/// <summary>The explicitly inventoried predefined predicates in ISO/IEC 13211 Parts 1–3.</summary>
internal static class IsoLanguageProfile
{
    internal static bool IsStandardPredicate(string name, int arity) =>
        (name, arity) switch
        {

            ("!", 0)
            or
            ("true", 0)
            or
            ("fail", 0)
            or
            ("false", 0)
            or
            ("repeat", 0)
            or
            ("flush_output", 0)
            or
            ("at_end_of_stream", 0)
            or
            ("nl", 0)
            or ("halt", 0) => true,
            (
                "var"
                    or "atom"
                    or "integer"
                    or "float"
                    or "atomic"
                    or "compound"
                    or "nonvar"
                    or "number"
                    or "callable"
                    or "ground"
                    or "acyclic_term"
                    or "current_predicate"
                    or "asserta"
                    or "assertz"
                    or "retract"
                    or "abolish"
                    or "retractall"
                    or "current_input"
                    or "current_output"
                    or "set_input"
                    or "set_output"
                    or "once"
                    or "throw"
                    or "current_module",
                1
            ) => true,
            (
                "="
                    or "\\="
                    or "unify_with_occurs_check"
                    or "subsumes_term"
                    or "=="
                    or "\\=="
                    or "@<"
                    or "@=<"
                    or "@>"
                    or "@>="
                    or "sort"
                    or "keysort"
                    or "=.."
                    or "copy_term"
                    or "term_variables"
                    or "is"
                    or "=:="
                    or "=\\="
                    or "<"
                    or "=<"
                    or ">"
                    or ">="
                    or "clause"
                    or "stream_property"
                    or "set_stream_position"
                    or "char_conversion"
                    or "current_char_conversion"
                    or "set_prolog_flag"
                    or "current_prolog_flag"
                    or "atom_length"
                    or "atom_chars"
                    or "atom_codes"
                    or "char_code"
                    or "number_chars"
                    or "number_codes"
                    or ":"
                    or "predicate_property",
                2
            ) => true,
            (
                "compare"
                    or "functor"
                    or "arg"
                    or "findall"
                    or "bagof"
                    or "setof"
                    or "op"
                    or "current_op"
                    or "atom_concat"
                    or "catch",
                3
            ) => true,
            ("sub_atom", 5) => true,
            ("call", >= 1 and <= 8) => true,
            (
                "close"
                    or "flush_output"
                    or "at_end_of_stream"
                    or "get_char"
                    or "get_code"
                    or "peek_char"
                    or "peek_code"
                    or "put_char"
                    or "put_code"
                    or "nl"
                    or "get_byte"
                    or "peek_byte"
                    or "put_byte"
                    or "read"
                    or "write"
                    or "writeq"
                    or "write_canonical",
                1
                    or 2
            ) => true,
            ("open", 3 or 4) => true,
            ("read_term" or "write_term", 2 or 3) => true,
            ("phrase", 2 or 3) => true,
            ("halt", 1) => true,
            _ => false,
        };

    internal static bool IsStandardEvaluable(string name, int arity) =>
        (name, arity) switch
        {
            ("pi", 0) => true,
            (
                "+"
                    or "-"
                    or "abs"
                    or "sign"
                    or "float_integer_part"
                    or "float_fractional_part"
                    or "float"
                    or "floor"
                    or "truncate"
                    or "round"
                    or "ceiling"
                    or "sqrt"
                    or "sin"
                    or "cos"
                    or "tan"
                    or "asin"
                    or "acos"
                    or "atan"
                    or "exp"
                    or "log"
                    or "\\",
                1
            ) => true,
            (
                "+"
                    or "-"
                    or "*"
                    or "//"
                    or "/"
                    or "rem"
                    or "mod"
                    or "div"
                    or "**"
                    or "^"
                    or "max"
                    or "min"
                    or "atan2"
                    or ">>"
                    or "<<"
                    or "/\\"
                    or "\\/"
                    or "xor",
                2
            ) => true,
            _ => false,
        };
}
