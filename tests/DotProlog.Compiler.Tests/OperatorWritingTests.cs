using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Writing terms in operator notation, and reading the result back.
/// </summary>
public sealed class OperatorWritingTests
{
    [Theory]
    [InlineData("1+2", "1+2")]
    [InlineData("1+2*3", "1+2*3")]
    [InlineData("(1+2)*3", "(1+2)*3")]
    [InlineData("2*3+1", "2*3+1")]
    [InlineData("1-2-3", "1-2-3")]
    [InlineData("1-(2-3)", "1-(2-3)")]
    [InlineData("2**3", "2**3")]
    [InlineData("a=b", "a=b")]
    [InlineData("a-1", "a-1")]
    [InlineData("f(1+2, a)", "f(1+2,a)")]
    [InlineData("[1+2, a-b]", "[1+2,a-b]")]
    [InlineData("[a|T]", "[a|_G3]")]
    [InlineData("{a, b}", "{a,b}")]
    [InlineData("a mod b", "a mod b")]
    [InlineData("Y is 1+2", "_G5 is 1+2")]
    [InlineData("\\+ a", "\\+a")]
    [InlineData("- (1)", "- (1)")]
    [InlineData("-(-(1))", "- - (1)")]
    [InlineData("1 - -2", "1- -2")]
    [InlineData("1 * (2, 3)", "1*(2,3)")]
    [InlineData("f(-)", "f(-)")]
    [InlineData("a:b:c", "a:b:c")]
    // The goal is bound to X, so no row may mention X: X = (X is 1+2) is a cyclic term, and writing
    // one runs until it exhausts memory. Unification has no occurs check, here as everywhere else.
    public void WritesOperatorNotation(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"X = ({goal}), write(X)"));

    // Cor.3 7.10.5: an operator atom is bracketed as an operand and bare as an argument or list
    // element; a prefix - brackets a non-negative number and an infix or postfix operand, since
    // - 1 is the integer -1 and - 1^2 is (-1)^2 in ISO; and , and | are bare as operators.
    [Theory]
    [InlineData("[:-, -]", "[:-,-]")]
    [InlineData("f(;, '|', ';;')", "f(;,'|',';;')")]
    [InlineData("[',']", "[',']")]
    [InlineData("(-) - (-)", "(-)-(-)")]
    [InlineData("(*) = (*)", "(*)=(*)")]
    [InlineData("- (-)", "- (-)")]
    [InlineData("-(1^2)", "- (1^2)")]
    [InlineData("-(a^2)", "- (a^2)")]
    [InlineData("-((1*2)^3)", "- ((1*2)^3)")]
    [InlineData("+((1*2)^3)", "+ (1*2)^3")]
    [InlineData("-(-(1))", "- - (1)")]
    [InlineData("-(-1)", "- -1")]
    [InlineData("-(1.5)", "- (1.5)")]
    [InlineData("+(1)", "+1")]
    [InlineData("-a", "-a")]
    [InlineData("'/*'", "'/*'")]
    [InlineData("'/**'", "'/**'")]
    [InlineData("//*", "//*")]
    [InlineData("'a b'+'c'", "'a b'+c")]
    [InlineData("f('a b', 'c d')", "f('a b','c d')")]
    public void WritesTheBracketsCorrigendum3Requires(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"X = ({goal}), writeq(X)"));

    [Theory]
    [InlineData(":- op(9, fy, fy), op(9, yf, yf).", "yf(fy(1))", "(fy 1)yf")]
    [InlineData(":- op(9, fy, fy), op(9, yf, yf).", "fy(yf(1))", "fy 1 yf")]
    [InlineData(":- op(9, fy, fy), op(9, yf, yf).", "yf(fy(yf(fy(1))))", "(fy (fy 1)yf)yf")]
    [InlineData(":- op(9, fy, fy), op(9, yfx, yfx).", "yfx(fy(1), 2)", "(fy 1)yfx 2")]
    [InlineData(":- op(9, yf, yf), op(9, xfy, xfy).", "yf(xfy(1, 2))", "(1 xfy 2)yf")]
    [InlineData(":- op(9, yf, yf), op(9, xfy, xfy).", "xfy(1, yf(2))", "1 xfy 2 yf")]
    [InlineData(":- op(100, fx, ' op').", "' op'('1')", "' op' '1'")]
    [InlineData(":- op(100, xf, 'f ').", "'f '(0)", "0 'f '")]
    [InlineData(":- op(9, xf, '$VAR').", "'$VAR'(-1)", "-1'$VAR'")]
    public void BracketsAndSpacesWhereAdjacentOperatorsWouldReadDifferently(string declarations, string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.Run($"{declarations}\n:- initialization((X = ({goal}), writeq(X))).\n"));

    [Fact]
    public void ClauseBodiesRead() => Assert.Equal("a:-b,c", PrologTestHost.RunGoal("X = (a :- b, c), write(X)"));

    [Fact]
    public void DisjunctionAndIfThenNest() => Assert.Equal("a;b->c;d", PrologTestHost.RunGoal("X = (a ; b -> c ; d), write(X)"));

    /// <summary>
    /// The property that matters: whatever <c>writeq/1</c> produces has to read back as the same
    /// term. A writer that only looks right is not enough — bracketing and spacing are exactly the
    /// places where it can look right and parse differently.
    /// </summary>
    [Theory]
    [InlineData("1+2*3")]
    [InlineData("(1+2)*3")]
    [InlineData("1-(2-3)")]
    [InlineData("- (1)")]
    [InlineData("-(-(1))")]
    [InlineData("1 - -2")]
    [InlineData("1 * (2, 3)")]
    [InlineData("- (a)")]
    [InlineData("\\+ (a, b)")]
    [InlineData("f(-, +, *)")]
    [InlineData("f((:-), (;))")]
    [InlineData("[a, b|c]")]
    [InlineData("{a, b}")]
    [InlineData("'hello world'+'A'")]
    [InlineData("(a :- b, c ; d)")]
    [InlineData("(a , b) - (c ; d)")]
    [InlineData("1 = (2 , 3)")]
    [InlineData("- - - 1")]
    [InlineData("f(a-(-1))")]
    [InlineData("(a->b;c)")]
    [InlineData("[1, -1, - (1)]")]
    [InlineData("[:-, -, (-) - (-), - (-)]")]
    [InlineData("-(1^2) + -((1*2)^3)")]
    [InlineData("+(1) - +(-1)")]
    [InlineData("f('/*', //*, '*/')")]
    public void WriteqRoundTripsThroughTheReader(string source)
    {
        var engine = new PrologEngine();

        Cell original = ReadOntoHeap(engine, source);
        var written = TermWriter.ToDisplayString(engine.Machine, original, quoted: true);
        Cell reread = ReadOntoHeap(engine, written);

        Assert.True(
            TermOrder.AreIdentical(engine.Machine, original, reread),
            $"{source} was written as {written}, which reads back as "
                + $"{TermWriter.ToDisplayString(engine.Machine, reread, quoted: true, ignoreOperators: true)} rather than "
                + $"{TermWriter.ToDisplayString(engine.Machine, original, quoted: true, ignoreOperators: true)}."
        );
    }

    [Theory]
    [InlineData("write_canonical(1+2*3)", "+(1,*(2,3))")]
    [InlineData("write_canonical([a, b])", "'.'(a,'.'(b,[]))")]
    [InlineData("write_canonical('a b')", "'a b'")]
    [InlineData("write_term(1+2, [ignore_ops(true)])", "+(1,2)")]
    [InlineData("write_term('a b', [quoted(true)])", "'a b'")]
    [InlineData("write_term('a b', [quoted(false)])", "a b")]
    [InlineData("write_term(1+2, [])", "1+2")]
    public void WritesCanonicalNotationOnRequest(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void AnUnknownWriteOptionIsReported() =>
        Assert.Equal(
            "domain_error(write_option,portray(true))",
            PrologTestHost.RunGoal("catch(write_term(x, [portray(true)]), error(E, _), write(E))")
        );

    private static Cell ReadOntoHeap(PrologEngine engine, string source)
    {
        ParseResult parsed = TermReader.ReadTerm(source, operators: engine.Program.Operators);
        Assert.Empty(parsed.Diagnostics);
        Assert.Single(parsed.Clauses);
        return TermReifier.ToHeap(engine.Machine, parsed.Clauses[0], []);
    }
}
