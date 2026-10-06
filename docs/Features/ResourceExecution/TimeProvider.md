# ResourceExecution: controlled time

Status: implementation in progress; local development verification is distinct from Linux RF3 qualification.
Decision: [ADR-115](../../ADR/ADR-115-time-provider.md).

## Goal and scope

All KeyLoad-owned C# current-time reads and elapsed/deadline/timer work use native .NET `TimeProvider`. Existing DateTime/DateTimeOffset static clocks are already rejected by KLD0022; hidden System-provider calls and Stopwatch still bypass controlled clocks. Use injected clocks in execution owners and select System only in constructor defaults or composition roots. Preserve UTC persisted timestamps, signed-request validity, RF3 ordering, native Orleans scheduling, monotonic budgets and actual benchmark provenance. No new packages or replacement database/transport dependencies. Provider control is explicitly authorized for tests. Runtime-host and business clocks may remain separately composed where their ownership differs.

## Requirements and acceptance

| Requirement | Acceptance | Evidence |
|---|---|---|
| REQ-TIME-001: current UTC is supplied by the execution owner's provider | AC-TIME-001: embedded JSON/native commands and due discovery observe an injected clock, including expiry/error and preserved state | real engine TUnit workflows; source inventory |
| REQ-TIME-002: elapsed work uses GetTimestamp/GetElapsedTime/TimestampFrequency | AC-TIME-002: budget expiry is deterministic and UTC changes do not replace monotonic elapsed authority | controlled-provider engine/read-cut workflows; timing source inventory |
| REQ-TIME-003: delays, deadline sources and managed timers use the owning provider | AC-TIME-003: cancellation, linked tokens, disposal and cleanup survive migration; native Orleans-owned timers retain native scheduling | source/call-site audit; Aspire unit/recovery/RF3 suites |
| REQ-TIME-004: direct clocks cannot re-enter runtime code | AC-TIME-004: KLD0022 rejects DateTime clocks, Environment ticks and Stopwatch timing while allowing native providers and unrelated symbols | real Roslyn analyzer workflows and exact spans |
| REQ-TIME-005: timing remains measurable and composition is explicit | AC-TIME-005: benchmark clocks preserve monotonic units and original measurements; diagnostics use matching provider frequency; defaults remain System | consumer build, source audit, existing benchmark protocol tests; performance claims require separate GitHub evidence |

## Ordered execution and ownership

1. TASK-TIME-001 (lead): record policy, contract and baseline solution build; preserve unrelated changes.
2. TASK-TIME-002 (engine worker): Core, Query, ZoneTree storage and Artifacts provider plumbing. Relevant existing tests and complete clock-controlled flows; report caller signature joins to lead.
3. TASK-TIME-003 (runtime worker): Server, Orleans and Replication provider injection and native timeout disposal. Preserve identity, native scheduler and RF3 acknowledgement contracts.
4. TASK-TIME-004 (timing worker): AppHost, Diagnostics and benchmarks use provider timestamps, frequency, delays and cleanup. No unrelated benchmark repairs or configuration changes.
5. TASK-TIME-005 (lead): analyzer enforcement, shared TestDatabase controlled clock, deterministic engine regressions, remaining test timing migration and all caller integration. Workers have disjoint production scopes; tests owned by lead except new worker-specific files explicitly reported before editing. All workers read their local AGENTS first; shared docs/config belong to lead.
6. TASK-TIME-006 (lead): inspect all diffs; scoped canonical formatting; solution Release build; Aspire analyzers/unit/recovery/RF3 suites; governance/diff checks. Collect available functional coverage through AppHost; unavailable coverage/CRAP remains unmeasured. Preserve every failure and qualification gate.

Validation skills in order: quality-ci (check native gates), analyzer-config (existing severity), format (canonical dotnet format), csharpier (N/A: dotnet format owner), code-analysis (Release analyzers), roslynator/meziantou/stylecop (review current package ownership; no new packages), complexity (existing source-owned limits plus changed-code review), crap-score (actual source-bound coverage only). Baseline and final outcomes will be recorded here after checks. A failed build is not completion. No worker commits; lead checkpoints only coherent scope without unrelated work.

```mermaid
flowchart LR
    Composition[System provider or controlled test clock] --> Owner[Execution owner]
    Owner --> UTC[GetUtcNow for persisted timestamps]
    Owner --> Elapsed[GetTimestamp and GetElapsedTime for budgets]
    Owner --> Timers[Provider delays and cancellation deadlines]
    Owner --> DB[Authorized request and node local database effects]
```
