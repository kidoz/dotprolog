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
        var verifyMemory = args.Length > 3 && args[3] == "--verify-memory";
        var verifyStorage = verifyMemory || (args.Length > 3 && args[3] == "--verify-storage");
        if (verifyMemory && (rounds < 20 || kind is not ("dynamic" or "dynamic-rules")))
            throw new ArgumentException("Memory verification needs at least twenty rounds of fact or rule churn.", nameof(args));
        if (verifyStorage && (rounds < 3 || kind is not ("dynamic" or "dynamic-rules" or "dynamic-held")))
            throw new ArgumentException("Storage verification needs at least three rounds of dynamic churn.", nameof(args));
        var action = kind switch
        {
            "dynamic" or "dynamic-held" => "assertz(item(I)), retract(item(I))",
            "dynamic-rules" => "assertz((item(I) :- (I > 0 -> true ; fail))), retract((item(I) :- _))",
            "float" => "_X is I+0.25",
            "big" => "_X is (1<<100)+I",
            "rational" => "_X is I rdiv 1000003",
            "text" => "number_string(I, _Text)",
            _ => throw new ArgumentException(
                "Choose dynamic, dynamic-rules, dynamic-held, float, big, rational or text.",
                nameof(args)
            ),
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
        if (verifyMemory)
            return VerifyMemory(engine, kind, mint, item, live, rounds, batch);
        using var reader = kind == "dynamic-held" ? new Machine(engine.Program) : null;
        var readerHeld = reader is not null;
        if (reader is not null)
        {
            reader.BeginCall();
            if (reader.Call(item, [reader.CreateVariable()]) != RunResult.Success)
                throw new InvalidOperationException("The retained reader must see the live clause.");
        }
        using var process = Process.GetCurrentProcess();
        var plateauRound = kind == "dynamic-held" ? rounds / 2 + 1 : 1;
        (int Code, int Constants, int CodeCapacity, int ConstantCapacity) plateau = default;
        Console.WriteLine(
            "kind,round,values,managed_bytes,working_set_bytes,allocated_bytes,lookup_ns,code_words,clause_records,texts,floats,bigs,rationals,constant_slots,code_capacity,constant_capacity,reader_held"
        );
        for (var round = 0; round <= rounds; round++)
        {
            if (reader is not null && round == plateauRound)
            {
                reader.BeginCall();
                readerHeld = false;
            }
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
            var storage = (
                engine.Program.CodeLength,
                engine.Program.ConstantCount,
                engine.Program.Code.Length,
                engine.Program.Constants.Length
            );
            if (round == plateauRound)
                plateau = storage;
            else if (verifyStorage && round > plateauRound && storage != plateau)
                throw new InvalidOperationException($"Dynamic code/constant storage did not plateau: {plateau} -> {storage}.");
            var symbols = engine.Program.Symbols;
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{kind},{round},{(long)round * batch},{managed},{process.WorkingSet64},{allocated},{lookup:F2},{engine.Program.CodeLength},{records},{symbols.AtomCount},{symbols.FloatCount},{symbols.BigIntegerCount},{symbols.RationalCount},{engine.Program.ConstantCount},{engine.Program.Code.Length},{engine.Program.Constants.Length},{(readerHeld ? 1 : 0)}"
                )
            );
        }
        return 0;
    }

    private static int VerifyMemory(PrologEngine engine, string kind, int mint, int item, Cell live, int rounds, int batch)
    {
        // Keep process sampling, console formatting and per-round logs out of the retained-memory
        // experiment. They have their own caching/allocation behaviour unrelated to the engine.
        const long tolerance = 64 * 1024;
        var minimum = long.MaxValue;
        var maximum = 0L;
        var machine = engine.Machine;
        (int Code, int Constants, int CodeCapacity, int ConstantCapacity) plateau = default;
        for (var round = 1; round <= rounds; round++)
        {
            machine.BeginCall();
            if (machine.Call(mint, [Cell.Integer60(batch), Cell.Integer60((long)(round - 1) * batch + 1)]) != RunResult.Success)
                throw new InvalidOperationException("Churn failed.");
            machine.BeginCall();
            if (machine.Call(item, [live]) != RunResult.Success)
                throw new InvalidOperationException("Live clause disappeared.");
            var storage = (
                engine.Program.CodeLength,
                engine.Program.ConstantCount,
                engine.Program.Code.Length,
                engine.Program.Constants.Length
            );
            if (round == 1)
                plateau = storage;
            else if (storage != plateau)
                throw new InvalidOperationException($"Dynamic code/constant storage did not plateau: {plateau} -> {storage}.");

            var managed = GC.GetTotalMemory(forceFullCollection: true);
            if (round > 10)
            {
                minimum = Math.Min(minimum, managed);
                maximum = Math.Max(maximum, managed);
            }
        }
        if (maximum - minimum > tolerance)
            throw new InvalidOperationException(
                $"Retained managed memory varied by {maximum - minimum} bytes after warmup; limit {tolerance}."
            );
        Console.WriteLine(
            "kind,rounds,values,managed_min,managed_max,managed_range,tolerance,code_words,constant_slots,code_capacity,constant_capacity"
        );
        Console.WriteLine(
            FormattableString.Invariant(
                $"{kind},{rounds},{(long)rounds * batch},{minimum},{maximum},{maximum - minimum},{tolerance},{plateau.Code},{plateau.Constants},{plateau.CodeCapacity},{plateau.ConstantCapacity}"
            )
        );
        return 0;
    }
}
