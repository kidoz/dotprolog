namespace DotProlog.LanguageServer;

// Protocol validation failures are distinct from defects in the compiler or transport.
internal sealed class InvalidParametersException : Exception
{
    public InvalidParametersException() { }

    public InvalidParametersException(string message)
        : base(message) { }

    public InvalidParametersException(string message, Exception innerException)
        : base(message, innerException) { }
}
