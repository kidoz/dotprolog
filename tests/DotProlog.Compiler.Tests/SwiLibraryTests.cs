using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The SWI-aligned library surface chartered by ADR 0041: library(error), library(ordsets),
/// library(assoc), and the smaller companions foldl/6, findall/4, numbervars/3, atom_to_term/3,
/// and tab/2.
/// </summary>
public sealed class SwiLibraryTests
{
    [Theory]
    [InlineData("must_be(integer, 3), write(ok)", "ok")]
    [InlineData("must_be(atom, foo), write(ok)", "ok")]
    [InlineData("must_be(boolean, true), write(ok)", "ok")]
    [InlineData("must_be(var, _), write(ok)", "ok")]
    [InlineData("must_be(list, [a, b]), write(ok)", "ok")]
    [InlineData("must_be(chars, [a, b]), write(ok)", "ok")]
    [InlineData("must_be(codes, [97, 98]), write(ok)", "ok")]
    [InlineData("must_be(oneof([a, b]), b), write(ok)", "ok")]
    [InlineData("must_be(between(1, 3), 2), write(ok)", "ok")]
    [InlineData("must_be(between(0.0, 1.0), 0.5), write(ok)", "ok")]
    [InlineData("must_be(positive_integer, 7), write(ok)", "ok")]
    [InlineData("must_be(nonneg, 0), write(ok)", "ok")]
    [InlineData("must_be(ground, f(a, b)), write(ok)", "ok")]
    [InlineData("must_be(list_or_partial_list, [a|_]), write(ok)", "ok")]
    [InlineData("must_be(text, [h, i]), write(ok)", "ok")]
    public void MustBeAcceptsValuesOfTheType(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    // The error split follows SWI's is_not/2: instantiation before type, uninstantiation for the
    // var type, type_error carrying the requested type name otherwise, and an existence error for
    // an unknown type name.
    [Theory]
    [InlineData("must_be(integer, foo)", "type_error(integer,foo)")]
    [InlineData("must_be(integer, _)", "instantiation_error")]
    [InlineData("must_be(var, foo)", "uninstantiation_error(foo)")]
    [InlineData("must_be(positive_integer, 0)", "type_error(positive_integer,0)")]
    [InlineData("must_be(between(1, 3), 5)", "type_error(between(1,3),5)")]
    [InlineData("must_be(oneof([a, b]), c)", "type_error(oneof([a,b]),c)")]
    [InlineData("must_be(list, [a|foo])", "type_error(list,[a|foo])")]
    [InlineData("must_be(list, [a|_])", "instantiation_error")]
    [InlineData("must_be(chars, [a, 1])", "type_error(chars,[a,1])")]
    [InlineData("must_be(ground, f(_))", "instantiation_error")]
    [InlineData("must_be(no_such_type, x)", "existence_error(type,no_such_type)")]
    [InlineData("must_be(_, x)", "instantiation_error")]
    public void MustBeRaisesTheSwiError(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), true), write(E)"));

    [Theory]
    [InlineData("is_of_type(integer, 1)", "yes")]
    [InlineData("is_of_type(integer, foo)", "no")]
    [InlineData("is_of_type(callable, f(x))", "yes")]
    [InlineData("is_of_type(pair, a-1)", "yes")]
    [InlineData("is_of_type(pair, a)", "no")]
    [InlineData("is_of_type(text, abc)", "yes")]
    [InlineData("is_of_type(negative_integer, -2)", "yes")]
    [InlineData("is_of_type(list_or_partial_list, [a|b])", "no")]
    public void IsOfTypeIsASemideterministicTest(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("type_error(integer, foo)", "type_error(integer,foo)")]
    [InlineData("domain_error(order, x)", "domain_error(order,x)")]
    [InlineData("existence_error(procedure, f/1)", "existence_error(procedure,f/1)")]
    [InlineData("permission_error(modify, static_procedure, p/0)", "permission_error(modify,static_procedure,p/0)")]
    [InlineData("instantiation_error(_)", "instantiation_error")]
    [InlineData("uninstantiation_error(x)", "uninstantiation_error(x)")]
    [InlineData("representation_error(max_arity)", "representation_error(max_arity)")]
    [InlineData("resource_error(memory)", "resource_error(memory)")]
    [InlineData("syntax_error(unterminated)", "syntax_error(unterminated)")]
    public void ErrorHelpersThrowTheIsoErrorTerm(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), true), write(E)"));

    [Fact]
    public void FoldlThreadsThreeListsThroughTheGoal() =>
        Assert.Equal(
            "21",
            PrologTestHost.Run(
                """
                sum3(A, B, C, S0, S) :- S is S0 + A + B + C.
                :- initialization((foldl(sum3, [1, 2], [3, 4], [5, 6], 0, R), write(R))).
                """
            )
        );

    [Fact]
    public void FindallWithATailPrefixesTheSolutions() =>
        Assert.Equal("[a,b,z]", PrologTestHost.RunGoal("findall(X, member(X, [a, b]), L, [z]), write(L)"));

    [Fact]
    public void FindallWithATailAndNoSolutionsIsTheTail() =>
        Assert.Equal("[z]", PrologTestHost.RunGoal("findall(X, fail, L, [z]), write(L)"));

    [Theory]
    [InlineData("numbervars(f(X, Y, X), 0, E), write(f(X, Y, X)-E)", "f(A,B,A)-2")]
    [InlineData("numbervars(g(X), 23, E), write(g(X)-E)", "g(X)-24")]
    [InlineData("numbervars(ground, 5, E), write(E)", "5")]
    public void NumbervarsBindsVariablesToNumberedTerms(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void NumbervarsValidatesItsStart() =>
        Assert.Equal("type_error(integer,a)", PrologTestHost.RunGoal("catch(numbervars(_, a, _), error(E, _), true), write(E)"));

    [Fact]
    public void AtomToTermReadsATermAndReportsItsBindings() =>
        Assert.Equal(
            "'X'-'Y'",
            PrologTestHost.RunGoal(
                "atom_to_term('foo(X, Y)', T, [NX = VX, NY = VY]), arg(1, T, A)," + " ( A == VX -> writeq(NX-NY) ; write(bad) )"
            )
        );

    [Fact]
    public void TabWritesSpacesToTheGivenStream() =>
        Assert.Equal("a  b", PrologTestHost.RunGoal("current_output(S), write(a), tab(S, 2), write(b)"));

    [Fact]
    public void TabRejectsAFractionalCount() =>
        Assert.Equal(
            "type_error(integer,1.5)",
            PrologTestHost.RunGoal("current_output(S), catch(tab(S, 1.5), error(E, _), true), write(E)")
        );

    [Theory]
    [InlineData("list_to_ord_set([c, a, b, a], S), write(S)", "[a,b,c]")]
    [InlineData("ord_empty(E), write(E)", "[]")]
    [InlineData("ord_union([a, c], [b, c, d], U), write(U)", "[a,b,c,d]")]
    [InlineData("ord_union([], [a], U), write(U)", "[a]")]
    [InlineData("ord_intersection([a, b, c], [b, d], I), write(I)", "[b]")]
    [InlineData("ord_intersection([a], [b], I), write(I)", "[]")]
    [InlineData("ord_subtract([a, b, c], [b], D), write(D)", "[a,c]")]
    [InlineData("ord_subtract([a, b], [a, b], D), write(D)", "[]")]
    [InlineData("ord_add_element([a, c], b, S), write(S)", "[a,b,c]")]
    [InlineData("ord_add_element([a, b], b, S), write(S)", "[a,b]")]
    [InlineData("ord_del_element([a, b, c], b, S), write(S)", "[a,c]")]
    [InlineData("ord_del_element([a, c], b, S), write(S)", "[a,c]")]
    public void OrderedSetOperationsMergeInOnePass(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("ord_memberchk(b, [a, b, c])", "yes")]
    [InlineData("ord_memberchk(z, [a, b])", "no")]
    [InlineData("ord_subset([b], [a, b])", "yes")]
    [InlineData("ord_subset([e], [a, b])", "no")]
    [InlineData("ord_subset([], [a])", "yes")]
    [InlineData("ord_disjoint([a], [b])", "yes")]
    [InlineData("ord_disjoint([a, b], [b, c])", "no")]
    [InlineData("ord_disjoint([], [])", "yes")]
    public void OrderedSetTestsAreSemideterministic(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("empty_assoc(A), put_assoc(k, A, 1, A1), get_assoc(k, A1, V), write(V)", "1")]
    [InlineData("empty_assoc(A), put_assoc(k, A, 1, A1), put_assoc(k, A1, 2, A2), get_assoc(k, A2, V), write(V)", "2")]
    [InlineData("list_to_assoc([b-2, a-1, c-3], A), assoc_to_list(A, L), write(L)", "[a-1,b-2,c-3]")]
    [InlineData("list_to_assoc([b-2, a-1, c-3], A), assoc_to_keys(A, K), write(K)", "[a,b,c]")]
    [InlineData("list_to_assoc([b-2, a-1, c-3], A), assoc_to_values(A, V), write(V)", "[1,2,3]")]
    [InlineData("list_to_assoc([b-2, a-1, c-3], A), min_assoc(A, K, V), write(K-V)", "a-1")]
    [InlineData("list_to_assoc([b-2, a-1, c-3], A), max_assoc(A, K, V), write(K-V)", "c-3")]
    [InlineData("ord_list_to_assoc([a-1, b-2], A), get_assoc(b, A, V), write(V)", "2")]
    [InlineData("list_to_assoc([], A), write(A)", "t")]
    public void AssocOperationsKeepTheKeyOrder(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void GetAssocFailsForAMissingKey() =>
        Assert.Equal(
            "no",
            PrologTestHost.RunGoal("empty_assoc(A), put_assoc(k, A, 1, A1), ( get_assoc(x, A1, _) -> write(yes) ; write(no) )")
        );

    [Theory]
    [InlineData("catch(list_to_assoc([a-1, a-2], _), error(E, _), true), write(E)", "domain_error(unique_key_pairs,[a-1,a-2])")]
    [InlineData("catch(list_to_assoc([a-1, x], _), error(E, _), true), write(E)", "type_error(pair,x)")]
    [InlineData(
        "catch(ord_list_to_assoc([b-1, a-2], _), error(E, _), true), write(E)",
        "domain_error(key_ordered_pairs,[b-1,a-2])"
    )]
    public void AssocConstructionValidatesItsPairs(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    // A hundred ascending and a hundred descending insertions force rotations in both
    // directions; the tree must still answer every key and enumerate in order.
    [Theory]
    [InlineData("numlist(1, 100, Ns)")]
    [InlineData("numlist(1, 100, Us), reverse(Us, Ns)")]
    public void AssocInsertionRebalancesInBothDirections(string listGoal) =>
        Assert.Equal(
            "57-100-first",
            PrologTestHost.Run(
                $$"""
                put_kv(K, A0, A) :- put_assoc(K, A0, K, A).
                :- initialization((
                    {{listGoal}},
                    empty_assoc(A0),
                    foldl(put_kv, Ns, A0, A),
                    get_assoc(57, A, V),
                    assoc_to_keys(A, Keys),
                    length(Keys, N),
                    Keys = [1|_],
                    write(V-N-first)
                )).
                """
            )
        );

    [Fact]
    public void StrictModeRejectsTheSwiLibrarySurface()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p :- must_be(integer, 1).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("must_be/2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModernModeSharesTheSwiLibrarySurface()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = output };

        Assert.True(engine.ConsultText(":- initialization((must_be(chars, \"ab\"), write(ok))).").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Theory]
    [InlineData("transpose_pairs([b-2, a-1], T), write(T)", "[1-a,2-b]")]
    [InlineData("transpose_pairs([a-1, b-1], T), write(T)", "[1-a,1-b]")]
    [InlineData("ord_union([[a, b], [b, c], [d]], U), write(U)", "[a,b,c,d]")]
    [InlineData("ord_union([], U), write(U)", "[]")]
    [InlineData("ord_intersection([[a, b, c], [b, c], [c, d]], I), write(I)", "[c]")]
    public void PairsAndSetFamiliesFold(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void IntersectionOfNoSetsFails() =>
        Assert.Equal("no", PrologTestHost.RunGoal("( ord_intersection([], _) -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("list_to_assoc([a-1, b-2, c-3], A), del_assoc(b, A, V, A1), assoc_to_list(A1, L), write(V-L)", "2-[a-1,c-3]")]
    [InlineData("empty_assoc(A), put_assoc(k, A, 1, A1), del_assoc(k, A1, _, A2), write(A2)", "t")]
    public void DelAssocRemovesTheKey(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void DelAssocFailsForAMissingKey() =>
        Assert.Equal(
            "no",
            PrologTestHost.RunGoal("list_to_assoc([a-1], A), ( del_assoc(x, A, _, _) -> write(yes) ; write(no) )")
        );

    // Deleting every key from a hundred-node tree exercises the join and the
    // deletion rebalance on both flanks.
    [Fact]
    public void DelAssocRebalancesDownToTheEmptyTree() =>
        Assert.Equal(
            "t",
            PrologTestHost.Run(
                """
                put_kv(K, A0, A) :- put_assoc(K, A0, K, A).
                del_kv(K, A0, A) :- del_assoc(K, A0, K, A).
                :- initialization((
                    numlist(1, 100, Ns),
                    empty_assoc(A0),
                    foldl(put_kv, Ns, A0, A),
                    foldl(del_kv, Ns, A, Empty),
                    write(Empty)
                )).
                """
            )
        );

    [Theory]
    [InlineData("findall(K-S, aggregate(sum(S), member(K-S, [a-1, a-2, b-5]), S), L), write(L)", "[a-3,b-5]")]
    [InlineData("aggregate(count, X^member(X, [x, y]), C), write(C)", "2")]
    [InlineData("aggregate(max(X), member(X, [3, 1, 2]), M), write(M)", "3")]
    [InlineData("aggregate(set(X), member(X, [b, a, b]), S), write(S)", "[a,b]")]
    [InlineData("aggregate(count, D, member(D, [a, b, a]), C), write(C)", "2")]
    [InlineData("aggregate_all(count, D, member(D-_, [a-1, a-2, b-1]), C), write(C)", "2")]
    [InlineData("aggregate_all(sum(S), D, member(D-S, [a-1, a-2, a-1, b-5]), Sum), write(Sum)", "8")]
    [InlineData("aggregate_all(bag(S), D, member(D-S, [b-2, a-1, b-2]), Bag), write(Bag)", "[1,2]")]
    public void AggregateFamiliesGroupAndDeduplicate(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void AggregateWithNoSolutionsFails() =>
        Assert.Equal("no", PrologTestHost.RunGoal("( aggregate(count, X^member(X, []), _) -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("variant(f(X, Y), f(A, B))", "yes")]
    [InlineData("variant(f(X, X), f(A, B))", "no")]
    [InlineData("variant(f(a), f(a))", "yes")]
    [InlineData("'?='(a, a)", "yes")]
    [InlineData("'?='(a, b)", "yes")]
    [InlineData("'?='(f(X), g(X))", "yes")]
    [InlineData("'?='(f(X), f(a))", "no")]
    [InlineData("'?='(X, Y)", "no")]
    public void VariantAndDecidedUnification(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("between(1, inf, X), X > 3, !, write(X)", "4")]
    [InlineData("between(1, infinite, 3), write(ok)", "ok")]
    [InlineData("findall(X, (between(1, inf, X), X >= 3, !), L), write(L)", "[3]")]
    public void BetweenAcceptsAnOpenUpperBound(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("format('~16r', [255])", "ff")]
    [InlineData("format('~16R', [255])", "FF")]
    [InlineData("format('~2r', [5])", "101")]
    [InlineData("format('~16r', [-255])", "-ff")]
    [InlineData("format('~36R', [35])", "Z")]
    public void FormatWritesRadixDirectives(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void FormatRadixRequiresTheRadix() =>
        Assert.Equal("domain_error(radix,0)", PrologTestHost.RunGoal("catch(format('~r', [5]), error(E, _), true), write(E)"));

    [Theory]
    [InlineData("char_type(a, alpha)", "yes")]
    [InlineData("char_type('1', alpha)", "no")]
    [InlineData("char_type('1', alnum)", "yes")]
    [InlineData("char_type('_', csym)", "yes")]
    [InlineData("char_type(' ', space)", "yes")]
    [InlineData("char_type(a, upper)", "no")]
    [InlineData("char_type('A', upper)", "yes")]
    [InlineData("char_type('.', period)", "yes")]
    [InlineData("char_type(a, ascii)", "yes")]
    [InlineData("char_type(',', punct)", "yes")]
    [InlineData("code_type(97, alpha)", "yes")]
    [InlineData("code_type(10, end_of_line)", "yes")]
    public void CharTypeClassifiesBoundCharacters(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("char_type('A', upper(L)), write(L)", "a")]
    [InlineData("char_type(a, lower(U)), write(U)", "A")]
    [InlineData("char_type('5', digit(W)), write(W)", "5")]
    [InlineData("char_type('A', to_upper(U)), write(U)", "a")]
    [InlineData("char_type(a, to_lower(L)), write(L)", "A")]
    [InlineData("char_type(a, code(C)), write(C)", "97")]
    [InlineData("code_type(65, to_upper(U)), write(U)", "97")]
    [InlineData("code_type(53, digit(W)), write(W)", "5")]
    public void CharTypeAnswersParametricCompanions(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("catch(char_type(a, bogus), error(E, _), true), write(E)", "domain_error(char_type,bogus)")]
    [InlineData("catch(char_type(_, alpha), error(E, _), true), write(E)", "instantiation_error")]
    [InlineData("catch(char_type(ab, alpha), error(E, _), true), write(E)", "type_error(character,ab)")]
    [InlineData("catch(code_type(a, alpha), error(E, _), true), write(E)", "type_error(integer,a)")]
    public void CharTypeValidatesItsArguments(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
}
