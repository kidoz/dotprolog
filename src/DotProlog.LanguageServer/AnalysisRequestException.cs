namespace DotProlog.LanguageServer;

// Expected request rejection; it must not invalidate a successfully analyzed workspace.
internal sealed class AnalysisRequestException : Exception
{
    internal AnalysisRequestException(int code, string message)
        : base(message) => Code = code;

    public AnalysisRequestException()
        : this(-32803, "Request failed.") { }

    public AnalysisRequestException(string message)
        : this(-32803, message) { }

    public AnalysisRequestException(string message, Exception innerException)
        : base(message, innerException) { }

    internal int Code { get; } = -32803;
}
