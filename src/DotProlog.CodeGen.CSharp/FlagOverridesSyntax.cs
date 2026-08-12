using System.Globalization;
using DotProlog.Runtime;

namespace DotProlog.CodeGen.CSharp;

/// <summary>Emits the C# spelling of a <see cref="PrologFlagOverrides"/> value into generated code.</summary>
internal static class FlagOverridesSyntax
{
    /// <summary>
    /// The engine constructor argument list for <paramref name="languageMode"/> and
    /// <paramref name="flagOverrides"/>. An empty override set emits the mode alone, so projects
    /// that set no flags keep byte-identical generated code.
    /// </summary>
    internal static string EngineArguments(PrologLanguageMode languageMode, PrologFlagOverrides flagOverrides)
    {
        var mode = string.Create(CultureInfo.InvariantCulture, $"global::DotProlog.Runtime.PrologLanguageMode.{languageMode}");
        if (flagOverrides.IsEmpty)
        {
            return mode;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{mode}, new global::DotProlog.Runtime.PrologFlagOverrides {{ DoubleQuotes = global::DotProlog.Runtime.DoubleQuotesMode.{flagOverrides.DoubleQuotes} }}"
        );
    }
}
