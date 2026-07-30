using System.Globalization;
using System.Text;

namespace DotProlog.CodeGen.CSharp;

/// <summary>Facts about C# the generator needs so that emitted names are always legal.</summary>
internal static class SyntaxFacts
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    };

    /// <summary>Whether <paramref name="name"/> is a C# keyword and so needs an <c>@</c> prefix.</summary>
    internal static bool IsKeyword(string name) => Keywords.Contains(name);

    /// <summary>Whether <paramref name="name"/> can be emitted verbatim as a C# identifier.</summary>
    internal static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || IsKeyword(name) || (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="name"/> is a dot-separated sequence of C# identifiers.</summary>
    internal static bool IsDottedIdentifierSequence(string name) => Array.TrueForAll(name.Split('.'), IsIdentifier);

    /// <summary>
    /// Whether a Prolog name becomes a C# identifier once underscores fold into Pascal or camel
    /// casing: every non-underscore character must be a letter or digit, and the first a letter.
    /// </summary>
    internal static bool MapsToIdentifier(string name)
    {
        bool first = true;

        foreach (char c in name)
        {
            if (c == '_')
            {
                continue;
            }

            if (first ? !char.IsLetter(c) : !char.IsLetterOrDigit(c))
            {
                return false;
            }

            first = false;
        }

        return true;
    }

    /// <summary>
    /// A C# string literal holding <paramref name="value"/>. Control characters and the line
    /// separators U+2028 and U+2029 are escaped as well as quotes and backslashes, because C#
    /// treats the separators as line breaks and a raw control character makes generated code
    /// unreadable at best.
    /// </summary>
    internal static string Literal(string value)
    {
        var text = new StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    text.Append("\\\\");
                    break;
                case '"':
                    text.Append("\\\"");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\u2028':
                case '\u2029':
                    text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }
}
