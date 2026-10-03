# Isolated Linux comparison agents

Owner direction 2026-10-03: replace three-OS qualification with Linux; run the same complete comparison workload in a separate isolated worker for every database, preserving its native cluster, emit JSON per worker, aggregate only after all workers finish and generate the performance site from those files.

## Framing and boundaries

The existing CI has Linux/macOS/Windows verification and one comparison-smoke job that brings multiple engines into one Aspire application. This does not provide runner-level per-database isolation. KeyLoad's RF3 product qualification and SQL/relational work remain required. Concurrent immutable-image, benchmark lifecycle and site work is visible in this checkout and must be preserved; root owns integration of shared contracts/workflows/docs.

## Options

1. Separate GitHub-hosted Linux matrix job for each engine/topology, one common executable suite with explicit strict target selection, complete JSON artifacts and a needs-gated aggregation job. Recommended: native runner/daemon isolation, no competing database workload, clear artifacts and observable failures.
2. Separate containers on one runner. Rejected: host scheduling, memory and I/O remain shared between engines during measurements.
3. Separate AI coding agents as benchmark execution infrastructure. Rejected: agent processes do not provide an isolated measurement host; human orchestration can inspect runner jobs but cannot replace them.

## Required contracts

Use existing community-engine capabilities, data/oracles and acknowledgement/read contracts. Run each currently supported topology and configured complete profile in isolation. Unsupported capability stays explicit; a failed measured capability fails the worker. Every report retains exact source SHA, run/attempt/job, target and workload identity. Aggregation rejects missing, duplicate, mixed-source or mixed-settings results before website evidence is eligible. Current site source remains independently qualified under BC-028; the measurement cohort must be complete and successful.

## Risks and questions

Current uncommitted lifecycle/image/site changes are independently owned; integration must retain their source identity and cleanup contracts. Existing comparison tests hard-code all-engine reports and need a strict target-aware path rather than weaker assertions. Free community topologies differ; unsupported cluster shapes must remain explicit. Memory/throughput equivalence comes from report contracts, not merely Linux labels. GitHub checks and complete worker evidence are mandatory; local scripts are static inspection only.

## Parallel discovery task graph

| Task | Owner | Permission | Dependency/start | Output/join |
|---|---|---|---|---|
| TASK-ISO-001 | sql_audit, inherited planning tier | read-only comparison source | now | target selection/common suite/report contract and regression plan |
| TASK-ISO-002 | models_audit, inherited planning tier | read-only scripts/site | now | strict aggregation/evidence ingestion contract |
| TASK-ISO-003 | gates_audit, inherited planning tier | read-only workflows | now | Linux/job matrix/Pages dependency design |
| TASK-ISO-004 | root | governance and planning writes | discoveries before implementation approval | acceptance, ordered plan, ADR/REQ/AC and exact worker ownership |

No implementation delegation begins before the root integrates these discoveries and approves the explicit requirements/ADR contract. Disjoint bounded implementation and meaningful TUnit tests follow. Existing exact-SHA GitHub run 37072003906 establishes source baseline; its comparison and RF3 failures are retained rather than hidden by the new orchestration.
