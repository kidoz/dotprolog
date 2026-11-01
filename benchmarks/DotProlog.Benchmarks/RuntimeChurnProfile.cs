using System.Diagnostics;
using System.Globalization;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Bounded, nonstationary churn measurement; deliberately separate from throughput benchmarks.</summary>
internal static class RuntimeChurnProfile
{
    internal static int Run(ReadOnlySpan<string> args)
    {
        var kind = args.Length > 0 ? args[0] : "dynamic";
        var rounds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 10;
        var batch = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1000;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batch);
        var action = kind switch
        {
            "dynamic" => "assertz(item(I)), retract(item(I))",
            "float" => "_X is I+0.25",
            "big" => "_X is (1<<100)+I",
            "rational" => "_X is I rdiv 1000003",
            "text" => "number_string(I, _Text)",
            _ => throw new ArgumentException("Choose dynamic, float, big, rational or text.", nameof(args)),
        };
        using var engine = new PrologEngine { Output = TextWriter.Null };
        engine.ConsultOrThrow(
            """
            :- dynamic item/1.
            item(live).
            mint(0, _) :- !.
            """
                + "\n"
                + $"mint(N,I) :- {action}, M is N-1, J is I+1, mint(M,J).",
            "churn.pl"
        );
        var mint = engine.Program.Symbols.InternFunctor("mint", 2);
        var item = engine.Program.Symbols.InternFunctor("item", 1);
        Cell live = Cell.Atom(engine.Program.Symbols.InternAtom("live"));
        var machine = engine.Machine;
        using var process = Process.GetCurrentProcess();
        Console.WriteLine(
            "kind,round,values,managed_bytes,working_set_bytes,allocated_bytes,lookup_ns,code_words,clause_records,texts,floats,bigs,rationals"
        );
        for (var round = 0; round <= rounds; round++)
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            if (round > 0)
            {
                machine.BeginCall();
                if (
                    machine.Call(mint, [Cell.Integer60(batch), Cell.Integer60((long)(round - 1) * batch + 1)])
                    != RunResult.Success
                )
                    throw new InvalidOperationException("Churn failed.");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            // End the churn call before measuring current-view lookup. Old snapshots may retain clauses.
            machine.BeginCall();
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < 10_000; i++)
            {
                machine.BeginCall();
                if (machine.Call(item, [live]) != RunResult.Success)
                    throw new InvalidOperationException("Live clause disappeared.");
            }
            var lookup = Stopwatch.GetElapsedTime(started).TotalNanoseconds / 10_000;
            var managed = GC.GetTotalMemory(forceFullCollection: true);
            process.Refresh();
            var records = 0;
            for (var clause = engine.Program.FindDynamic(item)!.First; clause is not null; clause = clause.Next)
                records++;
            var symbols = engine.Program.Symbols;
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{kind},{round},{(long)round * batch},{managed},{process.WorkingSet64},{allocated},{lookup:F2},{engine.Program.CodeLength},{records},{symbols.AtomCount},{symbols.FloatCount},{symbols.BigIntegerCount},{symbols.RationalCount}"
                )
            );
        }
        return 0;
    }
}
