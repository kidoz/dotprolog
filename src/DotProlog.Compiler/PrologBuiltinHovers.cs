using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>Finds builtin help at statically identifiable goal positions without executing source.</summary>
public static class PrologBuiltinHovers
{
    /// <summary>Analyzes supplied sources with their module/include contexts and the selected language mode.</summary>
    public static IReadOnlyList<PrologBuiltinHover> Analyze(
        IReadOnlyList<PrologLintSource> sources,
        PrologLanguageMode mode,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return
        [
            .. PrologCallSites
                .Analyze(PrologLintProgramModel.Build(sources), mode, cancellationToken)
                .Where(site => !site.Grammar)
                .Select(site => new PrologBuiltinHover(site.FileName, site.Range, site.Documentation)),
        ];
    }
}
