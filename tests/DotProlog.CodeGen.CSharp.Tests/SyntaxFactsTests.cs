namespace DotProlog.CodeGen.CSharp.Tests;

/// <summary>The name and literal facts that keep generated C# legal.</summary>
public sealed class SyntaxFactsTests
{
    [Theory]
    [InlineData("simple", "\"simple\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("a\rb", "\"a\\rb\"")]
    [InlineData("a\nb", "\"a\\nb\"")]
    [InlineData("a\tb", "\"a\\tb\"")]
    [InlineData("a\0b", "\"a\\u0000b\"")]
    [InlineData("a\u2028b", "\"a\\u2028b\"")]
    [InlineData("a\u2029b", "\"a\\u2029b\"")]
    public void LiteralEscapesEverythingCSharpCannotCarry(string value, string expected)
    {
        Assert.Equal(expected, SyntaxFacts.Literal(value));
    }

    [Theory]
    [InlineData("Pricing", true)]
    [InlineData("_private", true)]
    [InlineData("Has2Digits", true)]
    [InlineData("class", false)]
    [InlineData("2fast", false)]
    [InlineData("has-dash", false)]
    [InlineData("", false)]
    public void IsIdentifierAcceptsOnlyLegalNames(string name, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.IsIdentifier(name));
    }

    [Theory]
    [InlineData("Contoso", true)]
    [InlineData("Contoso.Rules", true)]
    [InlineData("Contoso.9rules", false)]
    [InlineData("Contoso.class", false)]
    [InlineData("Contoso..Rules", false)]
    public void IsDottedIdentifierSequenceChecksEveryPart(string name, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.IsDottedIdentifierSequence(name));
    }

    [Theory]
    [InlineData("in_stock", true)]
    [InlineData("_private_pred", true)]
    [InlineData("double", true)]
    [InlineData("2fast", false)]
    [InlineData("has-dash", false)]
    [InlineData("with space", false)]
    public void MapsToIdentifierFollowsThePascalFolding(string name, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.MapsToIdentifier(name));
    }
}
