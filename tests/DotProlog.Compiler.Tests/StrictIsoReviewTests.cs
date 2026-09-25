using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Local-standard review cases executed with the strict processor enabled.</summary>
public sealed class StrictIsoReviewTests
{
    [Theory]
    [InlineData(PrologLanguageMode.StrictIso, "on", "b")]
    [InlineData(PrologLanguageMode.Modern, "off", "a")]
    public void CharacterConversionStartsWithTheModeDefault(PrologLanguageMode mode, string flag, string predicate)
    {
        var engine = new PrologEngine(mode);
        Assert.True(engine.Query($"current_prolog_flag(char_conversion, {flag})").Prove());
        LoadResult loaded = engine.ConsultText(
            """
            :- char_conversion(a, b).
            a.
            """,
            "default-conversion.pl"
        );
        Assert.Empty(loaded.Diagnostics);
        Assert.True(engine.Query(predicate).Prove());
    }

    [Theory]
    [InlineData("1 \"+\" 2 * 3", "1 + 2 * 3")]
    [InlineData("\"-\" 7", "-(7)")]
    [InlineData("\"pair\"(left,right)", "pair(left,right)")]
    [InlineData("\"+\"(4,5)", "+(4,5)")]
    public void AtomValuedDoubleQuotesParticipateInParsing(string text, string expected)
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Input = new StringReader(text + ".") };
        LoadResult loaded = engine.ConsultText($":- set_prolog_flag(double_quotes, atom).\nquoted_term({text}).");
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Single(engine.Query($"quoted_term(T), T == ({expected})").Solutions());
        Assert.Single(engine.Query("set_prolog_flag(double_quotes, atom)").Solutions());
        Assert.Single(engine.Query($"read(T), T == ({expected})").Solutions());
    }

    [Theory]
    [InlineData("codes", "[97,98]")]
    [InlineData("chars", "[a,b]")]
    public void ListValuedDoubleQuotesKeepTheirRepresentation(string mode, string expected)
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);
        LoadResult loaded = engine.ConsultText($":- set_prolog_flag(double_quotes, {mode}).\nquoted_term(\"ab\").");
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Single(engine.Query($"quoted_term(T), T == {expected}").Solutions());
    }

    [Fact]
    public void InvalidMetaGoalIsRejectedBeforeItsFirstSideEffect()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output };

        Assert.Single(
            engine.Query("G = (write(unexpected), 17), catch(call(G), error(E,_), true), E == type_error(callable,G)").Solutions()
        );
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void InvalidReadOptionsDoNotConsumeTheNextTerm()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Input = new StringReader("kept.") };

        Assert.Single(
            engine
                .Query("catch(read_term(_, [bad]), error(E,_), true), E == domain_error(read_option,bad), read(kept)")
                .Solutions()
        );
    }

    [Fact]
    public void BoundOpenResultIsRejectedBeforeTruncatingTheFile()
    {
        var path = Path.GetTempFileName();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);
        try
        {
            File.WriteAllText(path, "retained");
            var atom = path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
            Assert.Single(
                engine
                    .Query($"catch(open('{atom}', write, bound), error(E,_), true), E == uninstantiation_error(bound)")
                    .Solutions()
            );
            Assert.Equal("retained", File.ReadAllText(path));
        }
        finally
        {
            engine.Machine.Streams.CloseAll();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(PrologLanguageMode.StrictIso)]
    [InlineData(PrologLanguageMode.Modern)]
    public void StandardReviewCasesPass(PrologLanguageMode mode)
    {
        var engine = new PrologEngine(mode);
        LoadResult loaded = engine.ConsultFile(Path.Combine(AppContext.BaseDirectory, "strict_iso_review.pl"));
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        string[] names = [.. engine.Query("iso_review_case(Name, _)").Solutions().Select(s => s["Name"].ToString())];
        Assert.Equal(125, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        List<string> failures = [];
        foreach (var name in names)
        {
            try
            {
                var count = engine.Query($"run_iso_review_case({name})").Solutions().Count();
                if (count != 1)
                {
                    failures.Add($"{name}: expected one solution, got {count}");
                }
            }
            catch (PrologException exception)
            {
                failures.Add($"{name}: {exception.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
