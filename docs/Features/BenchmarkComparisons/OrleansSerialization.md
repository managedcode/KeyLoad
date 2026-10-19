# serialization diagnostic acceptance

Goal: obtain authentic CPU/latency/allocation evidence for common serialization
paths before selecting bounded optimization across all operations. Actors: the
development executor and the generated BenchmarkDotNet child. No new database/API
capability, persisted format or authorization policy. Frontend N/A: artifacts are
diagnostics; published metrics retain their complete matrix gates.

```mermaid
flowchart LR
    T[Normal and scalar TUnit] --> S[Exact source and package capture]
    S --> B[External generated BenchmarkDotNet children]
    B --> R[24 original measurements and six corpora]
    R --> V[Closed result and provenance validation]
    V --> A[Diagnostic artifact]
```

| ID | Measurable contract | Test/evidence |
|---|---|---|
| AC-IS-PERF-001 | Three genuine public/unsealed fixtures use DocumentResult, CommandRequest and StorageMutation[]; two sizes 1024/16384; four actual typed RuntimeEncode/RuntimeDecode/JsonEncode/JsonDecode methods produce 24 distinct cases. Exact Unicode JSON, polymorphic mutation fields, vectors, raw bytes/empty/tombstone and optionals roundtrip identically. No fabricated engine, JSON fallback or ignored fields in comparator. | TUnit metadata and all six corpus/size roundtrips, decoded byte ownership; actual full BDN JSON/corpus hashes. |
| AC-IS-PERF-002 | The embedded generated-Dry test exercises exactly PointRead/CompositeKey/ExactCosine. Separate generated-Dry evidence proves external consumer execution; Dry never counts as measurement. | Original assertions with a narrowed fixture filter; separate real-child TUnit case and raw logs. |
| AC-IS-PERF-003 | Actual external BDN children run with MemoryDiagnoser, 2 launches, 3 warmups and 6 result iterations at a 200ms target. Original JSON/CSV/logs, generated build files, environment, exact source, corpus and assembly/package/source hashes are retained. Every one of the 24 cases has positive genuine Workload/Result operations/time, finite positive statistics, finite nonnegative allocation data and expected settings. Missing/failing/skipped/mixed results reject evidence. These runs are development evidence with explicit source/machine/settings and never refresh website performance evidence. | Validator regressions plus the retained raw artifacts of the development run. |
| AC-IS-PERF-004 | The diagnostics are never jobs, dispatch modes or dependencies of benchmarks.yml, which is reserved for end-to-end database comparisons. When another diagnostic mode is declared, conflicting selections fail before measurement and each lane excludes the other. Diagnostic mode cannot publish website/release evidence, and BDN rows never enter the database cluster aggregate. | Workflow source TUnit guards and the aggregate/identity tests. |

REQ-IS-PERF-001..004 map one-to-one above; REQ-IS-010/AC-IS-010 in
InternalSerialization link this complete diagnostic contract. ADR060 extends ADR047
public generated-consumer ownership. Local development TUnit/BDN experiments carry
explicit source/machine/settings; required Linux GitHub qualification and website
provenance stay mandatory. JSON is a typed codec diagnostic with a different execution
envelope and validation contract; no general engine/security/replication equivalence
is claimed. Warm metadata and steady-state results remain distinct from cold
initialization. The genuine Dry child has a 15-minute execution deadline. Cleanup
kills its process tree, cancels and closes owned pipes when their 15-second
observation threshold expires, and observes both capture tasks and every failure
before releasing files. Kernel file-I/O settlement has no BCL hard completion
guarantee; the final owned join is explicit, and the executor's job timeout is the
final bound. No numeric speed claim or regression threshold exists without a baseline;
further optimization requires that evidence and all normal/scalar/recovery/replicated
cluster guards. Unsupported formats, cancellation, byte budgets and trust boundaries
remain exactly the contract. Removing the additive fixtures rolls back this
diagnostic without touching user data.

## Native serialization measurements

The runtime uses the official generated Orleans serializer and native sessions.
There is no custom terminal type-admission fast path or wire walker to measure.
Diagnostics must retain exact source, package, workload, allocation and timing
identities. Retired private-parser allocation controls are not native codec
performance evidence. Existing corpus, execution ownership and independent
end-to-end comparison requirements remain mandatory.
