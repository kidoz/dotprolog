namespace DotProlog.Packages;

internal static class PackageModuleHeader
{
    public static bool Matches(ReadOnlySpan<char> text, ReadOnlySpan<char> name)
    {
        while (!text.IsEmpty)
        {
            text = text.TrimStart();
            if (text.StartsWith("%", StringComparison.Ordinal))
            {
                var end = text.IndexOf('\n');
                if (end < 0)
                    return false;
                text = text[(end + 1)..];
            }
            else if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = text[2..].IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                    return false;
                text = text[(end + 4)..];
            }
            else
                break;
        }
        if (!Take(ref text, ":-") || !Take(ref text, "module") || !Take(ref text, "("))
            return false;
        text = text.TrimStart();
        if (text.IsEmpty || text[0] is not (>= 'a' and <= 'z'))
            return false;
        var length = 1;
        while (length < text.Length && (char.IsAsciiLetterOrDigit(text[length]) || text[length] == '_'))
            length++;
        if (!text[..length].SequenceEqual(name))
            return false;
        text = text[length..];
        return Take(ref text, ",");
    }

    private static bool Take(ref ReadOnlySpan<char> text, ReadOnlySpan<char> token)
    {
        text = text.TrimStart();
        if (!text.StartsWith(token, StringComparison.Ordinal))
            return false;
        text = text[token.Length..];
        return true;
    }
}
