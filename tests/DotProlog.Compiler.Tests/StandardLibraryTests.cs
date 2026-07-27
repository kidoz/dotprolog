namespace DotProlog.Compiler.Tests;

/// <summary>
/// The library predicates every engine gets: text conversions, the list library, sorting, and
/// aggregation.
/// </summary>
public sealed class StandardLibraryTests
{
    [Theory]
    [InlineData("atom_length(hello, N), write(N)", "5")]
    [InlineData("atom_length('', N), write(N)", "0")]
    [InlineData("atom_codes(abc, C), write(C)", "[97,98,99]")]
    [InlineData("atom_codes(A, [104,105]), write(A)", "hi")]
    [InlineData("atom_chars(abc, C), write(C)", "[a,b,c]")]
    [InlineData("atom_chars(A, [h,i]), write(A)", "hi")]
    [InlineData("char_code(a, C), write(C)", "97")]
    [InlineData("char_code(C, 98), write(C)", "b")]
    [InlineData("upcase_atom('hi there', A), write(A)", "HI THERE")]
    [InlineData("downcase_atom('HI', A), write(A)", "hi")]
    [InlineData("atom_concat(foo, bar, A), write(A)", "foobar")]
    [InlineData("atom_concat(A, bar, foobar), write(A)", "foo")]
    [InlineData("atom_length(123, N), write(N)", "3")]
    public void ConvertsText(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("atom_number('42', N), write(N)", "42")]
    [InlineData("atom_number('-7', N), write(N)", "-7")]
    [InlineData("atom_number('3.5', N), write(N)", "3.5")]
    [InlineData("atom_number('1.0e3', N), write(N)", "1000.0")]
    [InlineData("atom_number('0x1f', N), write(N)", "31")]
    [InlineData("atom_number('0b101', N), write(N)", "5")]
    [InlineData("atom_number('0o17', N), write(N)", "15")]
    [InlineData("atom_number(A, 42), write(A)", "42")]
    public void ParsesNumbers(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("hello")]
    [InlineData("''")]
    [InlineData("'12a'")]
    [InlineData("'1.'")]
    [InlineData("'.5'")]
    [InlineData("'1 2'")]
    public void AtomNumberFailsOnTextThatIsNotANumber(string atom) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( atom_number({atom}, _) -> write(yes) ; write(no) )"));

    [Fact]
    public void NumberCodesRaisesASyntaxErrorRatherThanFailing()
    {
        // The distinction is the point of having both: atom_number/2 tests, number_codes/2 converts.
        Assert.Equal(
            "syntax_error(illegal_number)",
            PrologTestHost.RunGoal("catch(number_codes(_, \"zz\"), error(E, _), write(E))")
        );
    }

    [Fact]
    public void AtomConcatEnumeratesEverySplit() =>
        Assert.Equal("[-abc,a-bc,ab-c,abc-]", PrologTestHost.RunGoal("findall(A-B, atom_concat(A, B, abc), L), write(L)"));

    [Theory]
    [InlineData("sub_atom(hello, 1, 3, A, S), write(S/A)", "ell/1")]
    [InlineData("findall(S, sub_atom(abc, _, 2, _, S), L), write(L)", "[ab,bc]")]
    [InlineData("findall(B, sub_atom(banana, B, _, _, an), L), write(L)", "[1,3]")]
    [InlineData("findall(S, sub_atom(ab, _, _, _, S), L), write(L)", "[,a,ab,,b,]")]
    [InlineData("sub_atom(hello, 0, 2, _, S), write(S)", "he")]
    [InlineData("sub_atom(hello, B, 2, 0, S), write(B/S)", "3/lo")]
    public void FindsSubAtoms(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("atomic_list_concat([a, b, c], A), write(A)", "abc")]
    [InlineData("atomic_list_concat([a, 1, 2.5], A), write(A)", "a12.5")]
    [InlineData("atomic_list_concat([a, b], '-', A), write(A)", "a-b")]
    [InlineData("atomic_list_concat(L, '-', 'a-b-c'), write(L)", "[a,b,c]")]
    [InlineData("atomic_list_concat(L, ', ', 'a, b'), write(L)", "[a,b]")]
    public void JoinsAndSplits(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("length([a, b, c], N), write(N)", "3")]
    [InlineData("length(L, 2), write(L)", "[_G2,_G9]")]
    [InlineData("append([a], [b], L), write(L)", "[a,b]")]
    [InlineData("findall(X-Y, append(X, Y, [a, b]), L), write(L)", "[[]-[a,b],[a]-[b],[a,b]-[]]")]
    [InlineData("reverse([a, b, c], L), write(L)", "[c,b,a]")]
    [InlineData("nth0(1, [a, b, c], E), write(E)", "b")]
    [InlineData("nth1(1, [a, b, c], E), write(E)", "a")]
    [InlineData("findall(I, nth0(I, [a, b], _), L), write(L)", "[0,1]")]
    [InlineData("last([a, b, c], E), write(E)", "c")]
    [InlineData("findall(X, member(X, [a, b]), L), write(L)", "[a,b]")]
    [InlineData("memberchk(b, [a, b, b]), write(yes)", "yes")]
    [InlineData("select(b, [a, b, c], R), write(R)", "[a,c]")]
    [InlineData("subtract([a, b, c], [b], R), write(R)", "[a,c]")]
    [InlineData("intersection([a, b], [b, c], R), write(R)", "[b]")]
    [InlineData("union([a, b], [b, c], R), write(R)", "[a,b,c]")]
    [InlineData("delete([a, b, a], a, R), write(R)", "[b]")]
    [InlineData("numlist(1, 4, L), write(L)", "[1,2,3,4]")]
    [InlineData("sum_list([1, 2, 3], S), write(S)", "6")]
    [InlineData("max_list([1, 9, 3], M), write(M)", "9")]
    [InlineData("min_list([1, 9, 3], M), write(M)", "1")]
    [InlineData("max_member(M, [a, c, b]), write(M)", "c")]
    [InlineData("min_member(M, [b, a, c]), write(M)", "a")]
    [InlineData("list_to_set([a, b, a, c], S), write(S)", "[a,b,c]")]
    [InlineData("flatten([a, [b, [c, []]]], F), write(F)", "[a,b,c]")]
    [InlineData("findall(P, permutation([a, b], P), L), write(L)", "[[a,b],[b,a]]")]
    [InlineData("pairs_keys_values([a-1, b-2], K, V), write(K/V)", "[a,b]/[1,2]")]
    [InlineData("pairs_keys([a-1, b-2], K), write(K)", "[a,b]")]
    [InlineData("pairs_values([a-1, b-2], V), write(V)", "[1,2]")]
    public void ProvidesTheListLibrary(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void LengthEnumeratesListsOfGrowingLength() =>
        Assert.Equal("[0,1,2,3]", PrologTestHost.RunGoal("findall(N, (between(0, 3, N), length(_, N)), L), write(L)"));

    [Theory]
    [InlineData("msort([c, a, b, a], L), write(L)", "[a,a,b,c]")]
    [InlineData("sort([c, a, b, a], L), write(L)", "[a,b,c]")]
    [InlineData("sort([2, 1.0, 1], L), write(L)", "[1.0,1,2]")]
    [InlineData("sort(0, @>=, [1, 2, 2, 3], L), write(L)", "[3,2,2,1]")]
    [InlineData("sort(0, @>, [1, 2, 2, 3], L), write(L)", "[3,2,1]")]
    [InlineData("sort(2, @<, [f(1, b), f(2, a)], L), write(L)", "[f(2,a),f(1,b)]")]
    [InlineData("keysort([b-1, a-2, b-0], L), write(L)", "[a-2,b-1,b-0]")]
    [InlineData("predsort(compare, [c, a, b], L), write(L)", "[a,b,c]")]
    public void Sorts(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void KeysortLeavesEqualKeysInTheirOriginalOrder() =>
        Assert.Equal("[a-1,a-2,a-3]", PrologTestHost.RunGoal("keysort([a-1, a-2, a-3], L), write(L)"));

    [Fact]
    public void PredsortDropsElementsItsOrderCallsEqual()
    {
        // compare/3 on the second argument alone makes f(1,a) and f(2,a) equal, and predsort/3
        // keeps only the first of an equal run.
        Assert.Equal(
            "[f(1,a),f(3,b)]",
            PrologTestHost.Run(
                """
                by_second(O, T1, T2) :- arg(2, T1, A), arg(2, T2, B), compare(O, A, B).
                :- initialization((predsort(by_second, [f(1, a), f(2, a), f(3, b)], L), write(L))).
                """
            )
        );
    }

    [Theory]
    [InlineData("maplist(succ, [1, 2, 3], L), write(L)", "[2,3,4]")]
    [InlineData("maplist(atom, [a, b]), write(yes)", "yes")]
    [InlineData("include(integer, [a, 1, b, 2], L), write(L)", "[1,2]")]
    [InlineData("exclude(integer, [a, 1, b], L), write(L)", "[a,b]")]
    [InlineData("partition(integer, [a, 1, b], I, E), write(I/E)", "[1]/[a,b]")]
    [InlineData("foldl(plus, [1, 2, 3], 0, S), write(S)", "6")]
    [InlineData("once(member(X, [a, b])), write(X)", "a")]
    [InlineData("ignore(fail), write(yes)", "yes")]
    public void CallsGoalsWithExtraArguments(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void CallNAppendsArgumentsToACompoundGoal() =>
        Assert.Equal(
            "1+2+3",
            PrologTestHost.Run(
                """
                sum3(A, B, C) :- write(A), write(+), write(B), write(+), write(C).
                :- initialization(call(sum3(1), 2, 3)).
                """
            )
        );

    [Theory]
    [InlineData("aggregate_all(count, member(_, [a, b]), N), write(N)", "2")]
    [InlineData("aggregate_all(count(X), member(X, [a, b]), N), write(N)", "2")]
    [InlineData("aggregate_all(bag(X), member(X, [b, a, b]), L), write(L)", "[b,a,b]")]
    [InlineData("aggregate_all(set(X), member(X, [b, a, b]), L), write(L)", "[a,b]")]
    [InlineData("aggregate_all(sum(X), member(X, [1, 2, 3]), N), write(N)", "6")]
    [InlineData("aggregate_all(max(X), member(X, [1, 9, 3]), N), write(N)", "9")]
    [InlineData("aggregate_all(min(X), member(X, [1, 9, 3]), N), write(N)", "1")]
    public void Aggregates(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void AggregateAllSumOfNothingIsZero() =>
        Assert.Equal("0", PrologTestHost.RunGoal("aggregate_all(sum(X), member(X, []), N), write(N)"));

    [Fact]
    public void AggregateAllMaxOfNothingFails() =>
        Assert.Equal("no", PrologTestHost.RunGoal("( aggregate_all(max(X), member(X, []), _) -> write(X) ; write(no) )"));

    [Theory]
    [InlineData("succ(3, N), write(N)", "4")]
    [InlineData("succ(N, 4), write(N)", "3")]
    [InlineData("plus(1, 2, N), write(N)", "3")]
    [InlineData("plus(1, N, 3), write(N)", "2")]
    [InlineData("plus(N, 2, 3), write(N)", "1")]
    public void CountsUpAndDown(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void SuccOfNothingIsNotZero() =>
        Assert.Equal("no", PrologTestHost.RunGoal("( succ(_, 0) -> write(yes) ; write(no) )"));

    [Fact]
    public void CopyTermRenamesVariablesApartButKeepsTheirSharing() =>
        Assert.Equal("shared", PrologTestHost.RunGoal("copy_term(f(X, X), f(A, B)), A == B, A \\== X, write(shared)"));

    [Theory]
    [InlineData("term_variables(f(A, g(B), A), V), V == [A, B], write(yes)", "yes")]
    [InlineData("term_variables(foo, V), write(V)", "[]")]
    public void ReportsTermVariables(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void AConsultedDefinitionReplacesTheLibraryOne()
    {
        // Without this a program that defines its own member/2 would inherit the library's clauses
        // and get extra solutions, which is the failure mode a module system would otherwise prevent.
        Assert.Equal(
            "[mine]",
            PrologTestHost.Run(
                """
                member(mine, _).
                :- initialization((findall(X, member(X, [a, b]), L), write(L))).
                """
            )
        );
    }
}
