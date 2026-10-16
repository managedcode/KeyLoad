# Working database gates

Owner direction 2026-10-16 replaces the immediate execution order of the 105-task
plan with these two functional gates. The six-hour working window starts at
2026-10-15 22:18 UTC. Existing acceptance statuses and original evidence remain
unchanged. Broad audits, comparative benchmarks, coverage qualification and
unrelated task expansion follow a working database.

## Shared operation scenario

The same real .NET SDK and official MCP C# SDK scenario must run first against
one actual KeyLoad instance and then against three actual RF3 nodes. Aspire
AppHost owns each topology, Azure Table Storage membership through local
Azurite, image preparation, readiness, discovered endpoints and joined shutdown.
The one-instance development gate has its actual single-replica acknowledgement
contract; it is not evidence of RF3 durability.

Create three named resources of each supported model: typed relational tables,
documents, event stream sets, work queues, topics, graphs, time series and
analytics operations, blob stores, vector indexes and full-text indexes. Use
independent data and canonical entity references between models in one database.
Create eight persisted workload users with writer and reader permissions. A
separate persisted tenant-local operator has the native administrative role and
explicit grants for the three queues, as required for dead-letter redrive.
Verify that ordinary writers cannot redrive and that their refused attempts
leave the parked message unchanged. Do not trust caller-supplied roles or
introduce a separate authentication store.

Each model initializer creates its resources once. Whole-operation exercises
then interleave real SDK, official MCP and native T-SQL operations: create, read,
query, update, link, unlink and delete, with literal expected values, reference
integrity and current persisted authorization assertions. The document initializer
creates `gate_docs_0/anchor` before dependent models initialize. Iteration IDs are
unique across workers; each topology owns fresh storage and credentials.

Captured search must retain its original-cut movement-authority proof while
other model operations commit. Advancing the live store must neither force an
uncaptured lookup nor publish an old absence proof into the live authority
cache. Ranking uses the bounded captured source; terminal authorization and
ownership are checked again against current persisted state.

The target is at least 10,000 successful caller-visible database operations per
topology, with both SDK and MCP represented. Counts are incremented only after
the original public result reports success; every exercise also verifies its
expected state. This is a functional correctness scenario, not a throughput or
performance claim. A missing model, failed operation, wrong value, unauthorized
effect, unknown cleanup outcome or skipped scenario keeps its gate open.

## Execution and completion

Gate 1 must pass on the genuine single-instance Aspire topology before Gate 2
runs the same scenario on the genuine three-node cluster. Both gates retain
original native TUnit results, source and executable identities, model/operation
counts, actual node membership, failures and resource cleanup evidence. Source
presence, a successful build or successful startup alone cannot pass either gate.

Root owns common context, scenario composition, compiler, execution and commits.
The topology worker owns native one/three-node hosting; the fixture worker owns
test resource lifetime; the identity worker owns real persisted users. Companion
chats own distinct document/SQL/composition, events/queues/blobs and
graph/search/analytics modules. Available workers repair actual gate-blocking
defects while the other modules continue.

## Current local execution evidence

R446 Gate1 passed the original native single-instance case with 10,258 successful
public operations (5,288 SDK and 4,970 official MCP), 41 waves, eight persisted
workload users and one queue operator. Its terminal evaluator accepted the
joined runner/readers, original passing TRX, fixture cleanup and zero
source/executable/image identity drift. The evaluator is
`TestResults/stage61-native-join/root-r446-gate1-terminal-evaluation.original.json`
(SHA-256 `1c6502074f705ec35f44b6984c3cbbc8d6df9319b858c587b3d3071e6e77c7ea`).

R447 Gate2 failed after 2,332 successful public operations in nine waves.
Queue inspection through SDK and blob metadata through official MCP failed
with `OwnershipLost` at wave 4. A further 1,237 operations succeeded, but both
original failures keep Gate2 open. The captured full-text scope error from
R441 was not observed in this run. Its direct regression and seven related
whole-operation cases passed in R450; the full Release build passed without
warnings or errors. R440/R441 remain immutable historical evidence.

The gate deletes data, rows, blobs and edges. `DROP TABLE` and resource
catalog deletion are not implemented and remain part of open SQLC-DDL
requirements. Selected T-SQL and verified cross-model references do not
qualify complete unified SQL or cross-partition/failover behavior.

These are local functional gates. Original task acceptance remains 20 done and
85 in progress; complete Linux, recovery, fault/endurance, security, coverage,
performance and release qualification remain open. Neither gate alone declares
the complete product accepted or production ready.
