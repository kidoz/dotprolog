namespace DotProlog.Compiler.Tests;

/// <summary>
/// The predicates of the Prolog prologue, https://www.complang.tuwien.ac.at/ulrich/iso-prolog/prologue,
/// against the examples of its drafts, including those for <c>call_nth/2</c> and <c>countall/2</c>. Each
/// goal's solutions are collected, or its error reported, and compared with the draft's answer; the
/// cases that assume bounded integers are left out, since integers are unbounded here.
/// </summary>
public sealed class PrologueTests
{
    [Theory]
    [InlineData("member(X, [1,2])", "[member(1,[1,2]),member(2,[1,2])]")]
    [InlineData("member(X, [Y,Z|nonlist])", "[member(A,[A,B|nonlist]),member(C,[D,C|nonlist])]")]
    [InlineData("member(X, nonlist)", "[]")]
    [InlineData("append([a,b],[c,d], Xs)", "[append([a,b],[c,d],[a,b,c,d])]")]
    [InlineData("append([a], nonlist, Xs)", "[append([a],nonlist,[a|nonlist])]")]
    [InlineData("append([a], Ys, Zs)", "[append([a],A,[a|A])]")]
    [InlineData(
        "append(Xs, Ys, [a,b,c])",
        "[append([],[a,b,c],[a,b,c]),append([a],[b,c],[a,b,c]),append([a,b],[c],[a,b,c]),append([a,b,c],[],[a,b,c])]"
    )]
    [InlineData("length([a,b,c], Length)", "[length([a,b,c],3)]")]
    [InlineData("length(List, 5)", "[length([A,B,C,D,E],5)]")]
    [InlineData("between(1, 2, 0)", "[]")]
    [InlineData("between(1, 2, I)", "[between(1,2,1),between(1,2,2)]")]
    [InlineData("between(2, 1, I)", "[]")]
    [InlineData("between(I, I, 0)", "error(instantiation_error)")]
    [InlineData("between(1, I, 0)", "error(instantiation_error)")]
    [InlineData("between(I, -1, 0)", "error(instantiation_error)")]
    [InlineData("between(1, c, 0)", "error(type_error(integer,c))")]
    [InlineData("between(1+1,2,I)", "error(type_error(integer,1+1))")]
    [InlineData("select(X, [1,2], Xs)", "[select(1,[1,2],[2]),select(2,[1,2],[1])]")]
    [InlineData("select(X, [Y|nonlist], Xs)", "[select(A,[A|nonlist],nonlist)]")]
    [InlineData("succ(X, S)", "error(instantiation_error)")]
    [InlineData("succ(X, X)", "error(instantiation_error)")]
    [InlineData("succ(0, S)", "[succ(0,1)]")]
    [InlineData("succ(1, 1+1)", "error(type_error(integer,1+1))")]
    [InlineData("succ(X, 0)", "[]")]
    [InlineData("succ(-1, S)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("maplist(>(3), [1, 2])", "[maplist(>(3),[1,2])]")]
    [InlineData("maplist(>(3), [1, 2, 3])", "[]")]
    [InlineData("nth0(1, [a,b,c], E)", "[nth0(1,[a,b,c],b)]")]
    [InlineData("nth0(N, [a,b,c], E)", "[nth0(0,[a,b,c],a),nth0(1,[a,b,c],b),nth0(2,[a,b,c],c)]")]
    [InlineData("nth0(0, [A,B|non_list], E)", "[nth0(0,[A,B|non_list],A)]")]
    [InlineData("nth0(2, Es, E)", "[nth0(2,[A,B,C|D],C)]")]
    [InlineData("nth0(non_integer, Es, E)", "error(type_error(integer,non_integer))")]
    [InlineData("nth0(-1, Es, E)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("nth1(0, Es, E)", "[]")]
    [InlineData("foldl(append, [[1,2],[3],[4,5]], [],Xs)", "[foldl(append,[[1,2],[3],[4,5]],[],[4,5,3,1,2])]")]
    [InlineData("call_nth(true, Nth)", "[call_nth(true,1)]")]
    [InlineData("call_nth(false, Nth)", "[]")]
    [InlineData("call_nth(repeat, Nth), Nth >= 5, !", "[(call_nth(repeat,5),5>=5,!)]")]
    [InlineData("call_nth(( N = 1 ; N = 2 ), Nth)", "[call_nth((1=1;1=2),1),call_nth((2=1;2=2),2)]")]
    [InlineData("call_nth(true, non_integer)", "error(type_error(integer,non_integer))")]
    [InlineData("call_nth(true, 1.0)", "error(type_error(integer,1.0))")]
    [InlineData("call_nth(true, 0)", "[]")]
    [InlineData("call_nth(repeat, 0)", "[]")]
    [InlineData("call_nth(repeat, -1)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("call_nth(length(L,N), 3)", "[call_nth(length([A,B],2),3)]")]
    [InlineData("call_nth(inex, 0)", "[]")]
    [InlineData("call_nth(1, 0)", "[]")]
    [InlineData("call_nth(V, 0)", "[]")]
    [InlineData("call_nth(N = 1, N)", "[call_nth(1=1,1)]")]
    [InlineData("call_nth(N = -1, N)", "[]")]
    [InlineData("call_nth(repeat,1+1)", "error(type_error(integer,1+1))")]
    [InlineData("countall((X=1;X=2), N)", "[countall((A=1;A=2),2)]")]
    [InlineData("countall((true;true), N)", "[countall((true;true),2)]")]
    [InlineData("countall(G_0, N)", "error(instantiation_error)")]
    [InlineData("countall((length(L,5),nth0(_,L,_),nth0(_,L,_)), N)", "[countall((length(A,5),nth0(B,A,C),nth0(D,A,E)),25)]")]
    [InlineData("countall(N = 1, N)", "[countall(1=1,1)]")]
    [InlineData("countall(N = non_integer, N)", "[countall(1=non_integer,1)]")]
    [InlineData("countall(false, 1)", "[]")]
    [InlineData("countall(false, -1)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("countall(false, non_integer)", "error(type_error(integer,non_integer))")]
    [InlineData("nth0(1, [a,b,c], E, R)", "[nth0(1,[a,b,c],b,[a,c])]")]
    [InlineData("nth1(N, [a,b], E, R)", "[nth1(1,[a,b],a,[b]),nth1(2,[a,b],b,[a])]")]
    [InlineData("nth0(N, L, x, [a])", "[nth0(0,[x,a],x,[a]),nth0(1,[a,x],x,[a])]")]
    [InlineData("nth1(3, [a,b], E, R)", "[]")]
    [InlineData("nth1(0, [a], E, R)", "[]")]
    [InlineData("nth0(-1, [a], E, R)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("nth1(-1, [a], E, R)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("nth0(a, [a], E, R)", "error(type_error(integer,a))")]
    [InlineData("nth1(a, [a], E)", "error(type_error(integer,a))")]
    [InlineData("maplist(plus(1), [1,2], [2,3])", "[maplist(plus(1),[1,2],[2,3])]")]
    [InlineData("maplist(sub_atom, [abc], [1], [1], [1], [B])", "[maplist(sub_atom,[abc],[1],[1],[1],[b])]")]
    [InlineData("maplist(call, [sub_atom], [abc], [1], [1], [1], [B])", "[maplist(call,[sub_atom],[abc],[1],[1],[1],[b])]")]
    [InlineData(
        "maplist(call, [call], [sub_atom], [abc], [1], [1], [1], [B])",
        "[maplist(call,[call],[sub_atom],[abc],[1],[1],[1],[b])]"
    )]
    [InlineData("call_nth(member(X, [a,b,c]), 2)", "[call_nth(member(b,[a,b,c]),2)]")]
    [InlineData(
        "call_nth(member(X, [a,b,c]), N), N >= 2",
        "[(call_nth(member(b,[a,b,c]),2),2>=2),(call_nth(member(c,[a,b,c]),3),3>=2)]"
    )]
    [InlineData("succ(X, -1)", "error(domain_error(not_less_than_zero,-1))")]
    [InlineData("succ(a, X)", "error(type_error(integer,a))")]
    [InlineData("succ(3, 4)", "[succ(3,4)]")]
    public void AnswersAsThePrologueDraftsDo(string goal, string expected) =>
        Assert.Equal(
            expected,
            PrologTestHost.RunGoal(
                $"catch(findall(Goal_, (Goal_ = ({goal}), call(Goal_)), Answers_), error(Error_, _), Answers_ = error(Error_)), "
                    + "copy_term(Answers_, Shown_), numbervars(Shown_, 0, _), writeq(Shown_)"
            )
        );

    [Theory]
    [InlineData("call_nth(member(_, [a, b]), 1)")]
    [InlineData("call_nth(member(_, [a, b, c]), 2)")]
    [InlineData("countall(member(_, [a, b, c]), _)")]
    public void ABoundCountLeavesNoChoicePoint(string goal) =>
        Assert.Equal(
            "same",
            PrologTestHost.RunGoal(
                $"'$choice_points'(Before_), {goal}, '$choice_points'(After_), ( Before_ == After_ -> write(same) ; write(After_) )"
            )
        );
}
