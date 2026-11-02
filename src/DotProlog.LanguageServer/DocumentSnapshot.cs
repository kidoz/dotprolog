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
            return Path.GetFullPath(parsed.LocalPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidParametersException("Invalid file URI.", exception);
        }
    }
}
