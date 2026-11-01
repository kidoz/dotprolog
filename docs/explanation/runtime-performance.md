# Runtime performance measurements

These workloads separate warmed execution from program growth. Run them in Release from the
benchmark project directory so BenchmarkDotNet can identify the project unambiguously:

```sh
cd benchmarks/DotProlog.Benchmarks
dotnet run -c Release -- --filter '*TermOrderBenchmarks*' '*SortingBenchmarks*' '*HostCallBenchmarks*' --job Short
dotnet run -c Release -- --filter '*LargeClauseBenchmarks*' '*EngineBenchmarks*' --job Short
dotnet run -c Release -- --profile-churn dynamic 30 1000 --verify-storage
dotnet run -c Release -- --profile-churn dynamic-rules 30 1000 --verify-storage
dotnet run -c Release -- --profile-churn dynamic-held 30 1000 --verify-storage
dotnet run -c Release -- --profile-churn dynamic 100 1000 --verify-memory
dotnet run -c Release -- --profile-churn dynamic-rules 100 1000 --verify-memory
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
and intern-table counts. It also reports constant-pool slots, code/constant array capacities, and
whether a retained reader is active. `dynamic-rules` asserts clauses with conditional bodies;
`dynamic-held` keeps a reader open for the first half of the run, then resets it. The optional
`--verify-storage` flag checks that code/constant high-water marks and capacities stay fixed after
the first batch, or after reader release in the held-reader case. `--verify-memory` runs a separate
experiment without per-round logging or OS process sampling: after ten warmup batches, it checks
that full-GC managed memory stays within a 64 KiB range, as well as checking code/constant storage.
It requires at least twenty batches of fact or rule churn. Round zero includes initial JIT effects; use later rounds for warmed
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

### Initial clause-record collection measurements

In the initial comparison, before code/constant reuse, each of ten rounds minted 1,000 new values, with full GC and live-fact lookup measurements
between rounds. After 10,000 assert/retract pairs, both versions still had one live clause:

| Dynamic workload, final round | Before | After |
|---|---:|---:|
| Linked clause records | 10,001 | 1 |
| Retained managed bytes | 18,343,952 | 2,738,704 |
| Process working-set bytes | 69,386,240 | 58,720,256 |
| Average live-fact lookup including reset | 64,511 ns | 174 ns |
| Bytecode words | 96,484 | 96,484 |

The code stream started at 26,484 words. Its equal growth in both versions is the remaining
append-only limitation at that stage. The subsequent code-reclamation measurements below address
private dynamic emission while preserving pinned addresses.
The profile returns to the host between batches. An indefinitely active old call can still delay
record reclamation.

Numeric/text churn retained another 10,000 interned values in both versions. Candidate retained
managed growth was about 0.78 MiB for floats, 1.96 MiB for big integers, 1.53 MiB for rationals,
and 1.23 MiB for text. These totals include table capacity and runtime effects; they are not
per-value size contracts. The float table began with one constant and ended with 10,001 values;
the text table grew from 605 to 10,605 entries. Big-integer and rational tables grew from zero
to 10,000 entries. This is observable lifetime behavior, not a claim of bounded memory.

## Private dynamic code and constant reuse

A subsequent comparison used commit `2b84f7a` (record collection already enabled) as its baseline.
Both versions ran the extended profile, with only a constant-count getter added to the baseline
for instrumentation. Each experiment ran 30 batches of 1,000 unique fixnums over a bounded
predicate set. All three candidate `--verify-storage` runs passed.

| Workload after 30,000 assertions | Baseline code words | Candidate code words | Baseline constant slots | Candidate constant slots |
|---|---:|---:|---:|---:|
| Facts | 236,484 | 26,484 | 30,197 | 197 |
| Rules with conditional bodies | 896,519 | 26,519 | 60,199 | 199 |
| Facts with retained reader, released at round 16 | 236,484 | 26,484 | 30,197 | 197 |

Fact churn retained 3,349,832 managed bytes on the baseline and 2,309,888 on the candidate.
Conditional-rule churn retained 6,732,344 versus 2,410,384 bytes. After warmup, candidate code
and constant high-water marks and array capacities stayed fixed; process/GC/JIT measurements have
additional variation. Assertion still allocates syntax and detached clause objects: the final
fact batch allocated 1,920,000 bytes before and 1,952,000 after, including ownership metadata.
Reclamation reduces retained memory rather than eliminating compilation allocation.

The held-reader run deliberately allowed storage to grow while its old generation was protected.
After release, the candidate returned to its original code/constant append positions and stayed
there. Its arrays retained the peak capacity reached while the reader was active. Memory is bounded
by peak live/protected allocations and fragmentation, rather than by total historical assertions,
for this private dynamic storage and bounded workload. An indefinitely retained generation can
still require unbounded retention.

The separate `--verify-memory` experiments each ran 100,000 unique assertions in 100 batches.
Both passed the 64 KiB tolerance after ten warmup batches. Fact churn ranged from 2,299,672 to
2,310,560 managed bytes (10,888 bytes of variation); rule churn ranged from 2,332,600 to
2,335,312 (2,712 bytes). Code/constant append positions and capacities also stayed fixed. These
checks isolate engine retention from the OS-process sampling and per-round logging in the
full CSV profile.

A core ShortRun comparison against the same baseline remained allocation-free on all six cases.
Naive reverse measured 20.91 us before and 21.20 us after; countdown 565.99 us and 575.52 us;
the 20-fact scan 558.9 ns and 560.5 ns; typed host calls 24.47 ns and 22.64 ns. These indicative
timings do not establish a core speedup or a tight regression threshold.

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
top-level call no longer protects them. Private dynamic bytecode/constants are reused at that same
barrier. Static code, public/custom compilation results, host goals, compiled blocks and interned
text/numeric values retain their existing program lifetime. Long-running hosts processing unbounded
input therefore need [bounded engine lifetimes](../dotnet-integration.md#engine-lifetime-and-memory).

## Validation

On the final implementation including code/constant reuse, `just check` passed: CSharpier checked 358 files, the strict docs
build passed, the solution built with zero warnings/errors, and 3,621 tests passed with 285
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
five batches of 128 unique assert/retract pairs, verifies stable code/constant storage and a saved
host-compiled goal, and exercises stable compound-key sorting, cyclic comparison, and span-based
host calls. Publish checks reject product trim/AOT warnings.

These results verify the current host. Other operating systems and the broader opt-in SDK,
package-release, SWI differential and external compiled-conformance matrices were not rerun here.
