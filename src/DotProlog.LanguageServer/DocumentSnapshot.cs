namespace DotProlog.LanguageServer;

internal sealed record DocumentSnapshot(string Uri, string Text, int Version)
{
    internal static StringComparer KeyComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string Key(string uri)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            throw new InvalidParametersException();
        if (!parsed.IsFile)
            return parsed.AbsoluteUri;
        if (parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
            throw new InvalidParametersException();
        try
        {
            string path = parsed.LocalPath;
            // VS Code escapes the drive colon as %3A. Uri then treats the path as
            // Unix-style and retains its leading slash, even on Windows.
            if (
                OperatingSystem.IsWindows()
                && parsed.Host.Length == 0
                && path.Length >= 4
                && path[0] == '/'
                && char.IsAsciiLetter(path[1])
                && path[2] == ':'
                && path[3] == '/'
            )
                path = path[1..];
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidParametersException("Invalid file URI.", exception);
        }
    }
}
