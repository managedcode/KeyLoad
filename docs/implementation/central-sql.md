# One AI database: SQL, linked models and relational delivery

Owner direction2026-10-02: one server/database supports all models and their
links; SQL is central and performance has first priority. This is not a request
for a new single physical file. [ADR-054](../ADR/ADR-054-central-sql.md),
[ADR-055](../ADR/ADR-055-typed-relational-rows.md),
[acceptance](../../ai-database-sql.acceptance.md) and
[plan](../../ai-database-sql.plan.md) define the current delivery contract.

| Model | Existing canonical behavior | Current SQL / relational source stage | Remaining product boundary |
|---|---|---|---|
| Documents | CRUD/PATCH, unique/composite indexes, atomic receipts | Q1 SELECT; CALL canonical read/commit | Distributed/full declarative operators |
| Relational rows | New optional schema on Collection entity storage | Scalar types, primary/id equality, nonnullable/closed rows, native partition-local unique indexes; Q1 SELECT/CALL mutation | JOIN, FK/check/default/cascade, migration jobs and dedicated performance proof |
| Graph | Canonical EntityRef endpoints, bounded authorized BFS | CALL existing traversal/atomic edge commands; table rows remain endpoints | Declarative graph source/ranked fusion/cross-partition traversal |
| Vectors/text/hybrid | Revision-matching vectors, exact ranking/BM25/RRF, bounded authorization | CALL existing search/vector commands; table rows remain vector subjects | Fused SQL SEARCH operator, ANN/global ranking and measured acceleration |
| Events | Revision/generation/EventId append, bounded projected replay | CALL existing stream/event commands/read | Declarative EVENTS source and advanced schema/retention |
| Queues/topics | Fenced delivery, inbox, scheduling, group/checkpoint state | CALL existing inspect/receive/complete/process; reads never claim implicitly | Declarative queue views retain inspection rights; generic UPDATE internal state forbidden |
| Time series | UTC samples, latest/raw aggregates/dense windows | CALL existing typed reads/append | Declarative time-series SQL and qualified rollups/retention |
| Files/blobs | Canonical staged chunks, publish CAS, integrity/partial reads | CALL all ten existing operations; typed SDK blob surface joins HTTP/MCP | Cross-feature blob mutation batch/outbox and cluster-cut backup contract |

SQL invocation syntax (version1):

```sql
SELECT * FROM agents WHERE id = @id LIMIT 1;
CALL keyload_search_execute(@arguments);
CALL keyload_documents_commit(@arguments);
CALL keyload_blobs_read_range(@arguments);
```

Use the actual discovered canonical operation names. CALL's one named object
parameter is an argument envelope (`{"request":...}`, optional header `commandId`,
or `{}` for no-body reads); SELECT parameters remain Q1 scalars. Exactly one
statement compiles to exactly one canonical operation. SQL cannot call the SQL
adapter recursively, assert roles, replace IDs or bypass leases/revisions.
Results retain their actual canonical JSON shapes and safe domain errors.

The SQL wrapper reserves heavy-read DATA admission for every call. Direct control
routes retain their own reserved lane; SQL control CALL can be rejected during
DATA saturation. MCP hints therefore conservatively allow writes/consumption.
This stage does not claim full vendor SQL compatibility or arbitrary model JOIN.

## Qualification and performance

Pre-change main SHA b21c9eeaed701b7b2e267d4690fb3ac302c06c17 had queued
[run37065200835](https://github.com/managedcode/KeyLoad/actions/runs/37065200835).
Required baseline dispatched after planning:
[run37066160501](https://github.com/managedcode/KeyLoad/actions/runs/37066160501)
was cancelled before any job executed. The original37065200835 later completed
failure: all three OS build/format/governance/unit/scalar/recovery jobs and analyzer
rules passed; RF3 passed45/46 and comparisons2/4. The remaining snapshot/catch-up
failure shows Orleans directory/placement connection rejection to a stopped node.
Comparisons lifecycle remained Waiting, and two retained StreamAppend smoke cases
failed with unsupported ReadEventAsync. The root plan tracks exact jobs and cases.
Exact new-source qualification is pending; no test count or speed result is
inferred from authored cases, development builds or static governance.

The new RF3 sources cover genuine receive/ACK stable identities and forged/foreign
token rejection, pre-cancelled SDK/MCP SELECT followed by healthy reads, and actual
insufficient DATA byte admission on all three nodes with direct-control same-ID
progress. They do not establish in-flight server cancellation or concurrent lane
saturation; those qualifiers remain open until separately exercised.

The prior completed run37063262333 failed RF3 snapshot/catch-up and two TimeSeries
flows, Windows snapshot interruption and comparison startup. Its catalog cut
equality failure was repaired in later source. Root plan tracks each actual
failure; current runs are the authority, not historical fixes.

Native equality extraction now targets equivalent operand orientations, avoiding
full-scan requirements for reversed literal/parameter equality. Typed rows check
one final image, with raw patch scalar preflight before decimal canonicalization.
CALL grammar and bound target envelopes are rejected before gateway invocation.
SELECT/EXPLAIN preserve the canonical Q1 actor's single grammar validation before
any scan or effect, avoiding a second parse in the adapter.
No extra SQL executor/storage engine/full dataset materialization is introduced.
SIMD remains Search's portable .NET intrinsic validation stage; vector scoring
grouping is unchanged. Dedicated SQL/table/CALL workload and database-node
CPU/RAM/GC/contention/backlog measurements are still required alongside existing
client-process samples. Numeric coverage, endurance and power-loss gates remain
open; this work does not establish production readiness or performance superiority.
