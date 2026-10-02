# AI database and central SQL

Owner direction, 2026-10-02: KeyLoad unifies document, relational, graph, vector,
search, file/blob, queue and event operations for AI agents. SQL is the central
language and performance is first priority. Existing RF3, atomic partitions,
node-local storage and persisted authorization remain mandatory.

## Framing and options

1. Continue scalar document Q1 alone: insufficient relational and unified SQL
   coverage; reject as the completed product.
2. Add an unrelated SQL engine or consumer-side workarounds: duplicates storage,
   transactions and policy; reject.
3. Extend one versioned SQL/AST facade over existing authorized operations, with
   a real relational slice, bounded operators and exact capability disclosure:
   recommended. Start with the largest reusable correctness/performance gap
   identified by parallel source review, then carry each stage into CI.

The phrase about one file needs an explicit physical-format contract: the current
RF3 database owns multiple ZoneTree/journal/checkpoint files. Unified model and
one portable database artifact cannot be equated with one physical cluster file.
Record the storage-format decision without weakening journal/replica guarantees.

## Risks and open questions

Relational typed schemas/constraints and cross-model joins must preserve one
authorized cut and bounded resources. Queue SELECT must never claim a message;
mutations must compile to fenced/idempotent commands. Hidden fields cannot affect
predicate/order/join output. No performance superiority before comparable GitHub
latency/throughput/allocation/memory/contention/backlog artifacts.

## Discovery task graph (read-only; implementation follows frozen contracts)

| Task | REQ / AC | Owner / model | Permissions and start | Artifact / verification / join |
|---|---|---|---|---|
| TASK-AISQL-001 | draft REQ-AISQL-001 / AC-AISQL-001 | sql-audit / inherited high-capability | read-only; now | SQL/AST/server/client gap and bounded implementation recommendation; source pointers; join complete findings |
| TASK-AISQL-002 | draft REQ-AISQL-002 / AC-AISQL-002 | models-audit / inherited high-capability | read-only; now | relational/model/storage/file coverage and missing contracts; source pointers; join complete findings |
| TASK-AISQL-003 | draft REQ-AISQL-003 / AC-AISQL-003 | gates-audit / inherited high-capability | read-only; now | current exact-SHA GitHub gates, performance and test gap; commands/artifacts; join complete findings |
| TASK-AISQL-004 | all | root / planning and integration | docs/shared contracts; after discovery | acceptance, ADR, ordered plan and disjoint coding graph; static review |

No discovery task may edit, test locally, stash, commit/push, install dependencies,
or invent contract changes. All runtime qualification runs in GitHub Actions.
Existing dirty site source remains owned by concurrent work.
