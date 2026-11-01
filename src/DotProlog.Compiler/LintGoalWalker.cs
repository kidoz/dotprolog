using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Walks terms that occupy goal positions, without treating ordinary term data as code.</summary>
internal static class LintGoalWalker
{
    internal static IEnumerable<SyntaxTerm> Bodies(SyntaxTerm clause)
    {
        switch (clause)
        {
            case CompoundTerm { Name: ":-", Arity: 2 } rule:
                yield return rule.Arguments[1];
                break;
            case CompoundTerm { Name: ":-", Arity: 1 } directive:
                yield return directive.Arguments[0];
                break;
            case CompoundTerm { Name: "-->", Arity: 2 } grammar:
                foreach (SyntaxTerm body in GrammarGoals(grammar.Arguments[1]))
                {
                    yield return body;
                }

                break;
        }
    }

    internal static IEnumerable<CompoundTerm> Goals(SyntaxTerm body)
    {
        Stack<SyntaxTerm> pending = new();
        pending.Push(body);

        while (pending.TryPop(out SyntaxTerm? term))
        {
            if (term is not CompoundTerm goal)
            {
                continue;
            }

            yield return goal;

            if (goal.Name == ":" && goal.Arity == 2)
            {
                pending.Push(goal.Arguments[1]);
                continue;
            }

            if (goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->")
            {
                pending.Push(goal.Arguments[1]);
                pending.Push(goal.Arguments[0]);
                continue;
            }

            if (goal.Arity == 1 && goal.Name is "\\+" or "once" or "ignore" or "not" or "call")
            {
                pending.Push(goal.Arguments[0]);
                continue;
            }

            switch (goal.Name, goal.Arity)
            {
                case ("call", >= 2 and <= 8):
                case ("freeze", 2):
                case ("with_output_to", 2):
                case ("initialization", 1 or 2):
                case ("call_nth", 2):
                case ("countall", 2):
                    pending.Push(
                        goal.Arguments[
                            goal.Name == "initialization" ? 0
                            : goal.Name == "freeze" || goal.Name == "with_output_to" ? 1
                            : 0
                        ]
                    );
                    break;
                case ("catch", 3):
                    pending.Push(goal.Arguments[2]);
                    pending.Push(goal.Arguments[0]);
                    break;
                case ("forall", 2):
                case ("call_cleanup", 2):
                    pending.Push(goal.Arguments[1]);
                    pending.Push(goal.Arguments[0]);
                    break;
                case ("setup_call_cleanup", 3):
                    pending.Push(goal.Arguments[2]);
                    pending.Push(goal.Arguments[1]);
                    pending.Push(goal.Arguments[0]);
                    break;
                case ("findall", 3 or 4):
                case ("bagof", 3):
                case ("setof", 3):
                case ("aggregate_all", 3):
                case ("aggregate", 3):
                    pending.Push(goal.Arguments[1]);
                    break;
                case ("aggregate_all", 4):
                case ("aggregate", 4):
                    pending.Push(goal.Arguments[2]);
                    break;
            }
        }
    }

    private static IEnumerable<SyntaxTerm> GrammarGoals(SyntaxTerm body)
    {
        Stack<SyntaxTerm> pending = new();
        pending.Push(body);

        while (pending.TryPop(out SyntaxTerm? term))
        {
            if (term is not CompoundTerm compound)
            {
                continue;
            }

            if (compound.Name == "{}" && compound.Arity == 1)
            {
                yield return compound.Arguments[0];
                continue;
            }

            if (compound.Arity == 2 && compound.Name is "," or ";" or "|" or "->" or "*->")
            {
                pending.Push(compound.Arguments[1]);
                pending.Push(compound.Arguments[0]);
            }
            else if (compound.Name == "\\+" && compound.Arity == 1)
            {
                pending.Push(compound.Arguments[0]);
            }
        }
    }
}
