using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal static class DocumentHover
{
    internal static JsonObject? Convert(SourceOutline? outline, int line, int character, bool markdown)
    {
        if (outline is null)
            return null;
        var positions = new SourcePositions(outline.Text);
        int? offset = positions.Offset(line, character);
        if (offset is null)
            return null;
        var matches = outline
            .Hovers.Where(site => offset >= site.Range.Start && offset < site.Range.Start + site.Range.Length)
            .ToArray();
        if (matches.Length == 0 || matches.Any(site => site.Documentation is null))
            return null;
        var first = matches[0];
        if (matches.Any(site => site.Documentation != first.Documentation || site.Range != first.Range))
            return null;
        var help = first.Documentation!;
        string text = markdown ? "```prolog\n" + help.Signature + "\n```\n\n" : help.Signature + "\n\n";
        text += help.Description;
        if (help.Availability is not null)
            text += "\n\n" + help.Availability;
        return new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = markdown ? "markdown" : "plaintext", ["value"] = text },
            ["range"] = positions.Range(first.Range),
        };
    }
}
