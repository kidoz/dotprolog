# Runtime performance measurements

These workloads separate warmed execution from program growth. Run them in Release from the
benchmark project directory so BenchmarkDotNet can identify the project unambiguously:

```sh
cd benchmarks/DotProlog.Benchmarks
dotnet run -c Release -- --filter '*TermOrderBenchmarks*' '*SortingBenchmarks*' '*HostCallBenchmarks*' --job Short
dotnet run -c Release -- --filter '*LargeClauseBenchmarks*' '*EngineBenchmarks*' --job Short
dotnet run -c Release -- --profile-churn dynamic 10 1000
dotnet run -c Release -- --profile-churn float 10 1000
dotnet run -c Release -- --profile-churn big 10 1000
dotnet run -c Release -- --profile-churn rational 10 1000
dotnet run -c Release -- --profile-churn text 10 1000
```

Comparison covers atoms, fixnums, finite compounds and cycles. Sorting covers 32 and 1,024 atomic or
compound keys; its timing includes list construction. Large-table measurements cover 20 and 4,096
clauses on consulted bytecode, generated C#, and direct IL. Roslyn compilation, installation and
JIT warmup are outside the timed operations. Hit selects the last clause, miss rejects an absent
key, and scan exhausts every clause. These tables contain distinct integer keys; wildcard-heavy
and heavily duplicated tables need separate measurements.

The churn profile is a bounded experiment rather than a stationary BenchmarkDotNet case. Each
round creates another batch of distinct values or asserts and retracts another batch of facts.
It reports retained managed bytes after full GC, process working set, bytes allocated while
minting, average latency of 10,000 live-fact lookups, emitted code words, linked clause records,
and intern-table counts. Round zero includes initial JIT effects; use later rounds for warmed
lookup comparisons. Working set includes native/JIT/runtime memory and varies with the host.
Managed allocation and retained memory measure different things.

## Local before/after evidence

Measured on 2026-11-01, Apple M4 Max, macOS Arm64, .NET SDK 10.0.403 / runtime 10.0.14,
BenchmarkDotNet 0.15.8 ShortRun. The baseline was release commit `5913d62` in a separate worktree
with only the new measurement fixtures and symbol-count instrumentation added. Candidate runs
used another worktree to avoid unrelated scratch projects during BenchmarkDotNet discovery.

| Workload | Before mean | After mean | Before allocation | After allocation |
|---|---:|---:|---:|---:|
| Compare integers | 13.24 ns | 2.97 ns | 136 B | 0 B |
| Compare atoms | 15.41 ns | 3.52 ns | 136 B | 0 B |
| Compare finite compounds | 37.74 ns | 20.76 ns | 304 B | 0 B |
| Compare cycles | 39.97 ns | 24.08 ns | 304 B | 0 B |
| Typed host call, two integers | 29.12 ns | 23.32 ns | 208 B | 0 B |
| Sort 1,024 integers | 495.17 us | 312.12 us | 1,607.17 KB | 50.61 KB |
| Keysort 1,024 integer keys | 457.35 us | 326.65 us | 1,469.21 KB | 48.52 KB |
| Sort 1,024 compound keys | 759.06 us | 583.45 us | 2,888.94 KB | 50.61 KB |
| Keysort 1,024 compound keys | 702.17 us | 589.26 us | 2,639.09 KB | 48.52 KB |

The 4,096-clause bound lookup measurements use the last integer key and an absent integer key.
Every entry below measured zero warmed allocation:

| Backend | Before hit | After hit | Before miss | After miss |
|---|---:|---:|---:|---:|
| Bytecode | 1,812.38 ns | 33.62 ns | 1,714.28 ns | 35.18 ns |
| Generated C# | 505,930 ns | 43.81 ns | 459,158 ns | 35.87 ns |
| Direct IL | 1,792.10 ns | 42.05 ns | 1,757.66 ns | 34.87 ns |

Full scans of 4,096 clauses measured 70.95 us on bytecode, 656.86 us on generated C#, and
421.96 us on direct IL. These scans still expose compiled dispatch overhead. Indexing does not
remove the need to visit all solutions. Large compiled-table baselines had substantial timing
variance; the exact speedup factor is not a regression threshold.

A later baseline recheck measured naive reverse at 20.35 us, countdown at 542.23 us, and the
20-fact backtracking workload at 550.9 ns, versus candidate means of 20.75 us, 546.57 us,
and 565.2 ns. All remained allocation-free. These small timing differences do not establish a
core-engine speedup; they include possible reset/index bookkeeping costs and host variation.

### Churn and retained memory

Each of ten rounds minted 1,000 new values, with full GC and live-fact lookup measurements
between rounds. After 10,000 assert/retract pairs, both versions still had one live clause:

| Dynamic workload, final round | Before | After |
|---|---:|---:|
| Linked clause records | 10,001 | 1 |
| Retained managed bytes | 18,343,952 | 2,738,704 |
| Process working-set bytes | 69,386,240 | 58,720,256 |
| Average live-fact lookup including reset | 64,511 ns | 174 ns |
| Bytecode words | 96,484 | 96,484 |

The code stream started at 26,484 words. Its equal growth in both versions is the remaining
append-only limitation; record reclamation does not reclaim emitted code or constant-pool entries.
The profile returns to the host between batches. An indefinitely active old call can still delay
record reclamation.

Numeric/text churn retained another 10,000 interned values in both versions. Candidate retained
managed growth was about 0.78 MiB for floats, 1.96 MiB for big integers, 1.53 MiB for rationals,
and 1.23 MiB for text. These totals include table capacity and runtime effects; they are not
per-value size contracts. The float table began with one constant and ended with 10,001 values;
the text table grew from 605 to 10,605 entries. Big-integer and rational tables grew from zero
to 10,000 entries. This is observable lifetime behavior, not a claim of bounded memory.

## Limits and interpretation

ShortRun has one launch and three measured iterations. Treat its timing as indicative evidence;
do not use these means as tight regression thresholds. Recheck with repeated, interleaved baseline
and candidate runs before attributing small timing differences to a change.

Atomic comparison and warmed finite/cyclic compound comparison avoid traversal-scratch allocation.
Arbitrary-precision numeric comparison can still allocate intermediate big-integer products.
Sorting continues to allocate its element and result buffers. Index buckets add installation-time
memory proportional to the clause count in exchange for faster bound selection; scanning all
solutions still requires work proportional to the number of clauses.

Dynamic collection releases dead clause records and detached terms only after the oldest retained
top-level call no longer protects them. Bytecode and constants remain append-only. Interned text
and numeric values also retain their program lifetime. Long-running hosts processing unbounded
input therefore need [bounded engine lifetimes](../dotnet-integration.md#engine-lifetime-and-memory).

## Validation

On the final implementation, `just check` passed: CSharpier checked 352 files, the strict docs
build passed, the solution built with zero warnings/errors, and 3,602 tests passed with 285
opt-in skips. The default gate does not execute NativeAOT tests.

After building `src/DotProlog.Build.Tasks` in Release, these additional commands passed on
macOS Arm64, with zero skips:

```sh
MSBUILDDISABLENODEREUSE=1 DOTPROLOG_RUN_AOT_TESTS=1 dotnet test --project tests/Integration -c Release -- --filter-class Integration.Tests.NativeAotAcceptanceTests
MSBUILDDISABLENODEREUSE=1 DOTPROLOG_RUN_AOT_TESTS=1 dotnet test --project tests/Integration -c Release -- --filter-class Integration.Tests.DirectIlCompilerTests
```

The first ran four native acceptance tests, including StrictIso and Modern generated/consulted
execution, runtime updates, and the 763-case independent ISO corpus. The second published and ran
direct IL without its source or managed assemblies. The acceptance sample additionally exercises
128 assert/retract pairs followed by a new host call, stable compound-key sorting, cyclic
comparison, and span-based host calls. Publish checks reject product trim/AOT warnings.

These results verify the current host. Other operating systems and the broader opt-in SDK,
package-release, SWI differential and external compiled-conformance matrices were not rerun here.
