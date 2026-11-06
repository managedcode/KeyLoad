# ADR-146: Group commit

Status: Accepted. Current source; exact-source distributed qualification remains a separate gate.

## Context

Concurrent commands in one atomic partition can share physical transaction work
while retaining individual outcomes and canonical order. A public document batch
is one command containing several mutations. Unrelated partitions must remain
able to publish while another partition waits for a copy.

## Decision

`PartitionCommitGrain` owns one logical atomic partition's command queue.
`DatabaseCommitGrain` owns metadata commands and, in shared-volume mode, the
physical session acquisition. These ordinary grains are reentrant so submissions
and read barriers can interleave while one joined drain waits for publication.
Each queue has one drain; a partition's next batch follows its preceding batch.
A batch is the commands already queued when that drain takes them. Commands which
arrive during publication enter the next batch.

After completing a retained copy gap, the command owner selects a current primary
which covers both its partition prefix and metadata authority. Data and catalog
decisions use a common physical primary so native serializable conflicts protect
fresh authorization and resource lifetimes. This is full-copy execution, not
independent partition primaries or physical query fan-out.

Local primary execution borrows the original physical session through DI.
Remote primary execution uses `ISiloDatabaseService.CommitBatchAsync`; copies use
`ISiloDatabaseService.ApplyReplicaBatchAsync`. The physical session's original
work ledger joins every publication before close. File handles never leave that
owner. No ordinary activation is created solely to apply a physical batch.

The primary prepares commands once and publishes their effects in one native
ZoneTree transaction. An `IAtomicActionStaging` action sees earlier actions'
writes; resetting an action discards only its own changes. Each newly retained
outcome advances that partition's canonical prefix. A retry replays its original
outcome without another sequence. Preparation stops before a later command whose
identity or preparation requires a new batch. A failure outside a later command's
persisted outcome leaves it for the next batch; the first command still decides
as it would alone. Business time never decreases within a partition batch.

The same transaction retains `StorageReplicaBatch`: exact decided mutations,
original outcome bytes, partition predecessor and final prefix, batch identity,
and observed dependency prefixes. Its `Applied` array contains only typed
sequence/success/error decisions. Copies never prepare or authorize the business
command again. They validate incarnation, batch identity, exact predecessor and
required dependency prefixes before atomically applying the original effects.
A duplicate batch does not publish again. Native physical timestamps are not
copied or used as canonical sequence counters.

A copy missing a dependency defers before effects, waits outside its transaction
for that earlier prefix to reach the required copies, then retries against a fresh
native cut. Catalog decisions depend on earlier observed partition data; data
decisions depend on catalog state and foreign resource partitions actually read.
This prevents a catalog deletion or foreign-vertex operation from overtaking its
source effects on a copy.

Every required copy must confirm the same prefix and decisions before the command
owner attaches its observed persisted-copy count. Only the primary returns full
results; exact persisted outcome bytes remain on every copy. A missing selected
result or disagreement yields `UnknownWriteOutcome`. Failure before primary
dispatch leaves writes unstarted; failure after dispatch may leave an outcome
unknown even when no response arrived. A stable command ID resolves retries.

A failed copy invalidates the owner roster. The existing joined drain and a typed
Orleans grain timer complete the last retained exact batch without another
foreground write. Read barriers may proceed on an advanced complete owner, and
other partition drains remain independent. Larger gaps, unavailable required
copies and divergent predecessors refuse explicitly. This mechanism does not
rebuild an empty replica or establish general lost-copy recovery.

## Consequences and qualification

All-copy acknowledgement retains the selected native persistence barrier from
[ADR-003](ADR-003-durability-ack-barrier.md). Grouping adds no disk sync per command;
explicit `IAtomicStore.SyncAsync` awaits native WAL APIs. Native snapshots, conflict
handling, cache maintenance and merge scheduling remain ZoneTree responsibilities.

Shared-volume mode keeps one ordinary physical owner and one open ZoneTree;
independent request contexts borrow its joined session. It requires one persisted
copy. Actual deployed filesystem exclusion and flush behavior remain separate
qualification gates.

Grouped publication, exact replay, wrong acknowledgements, causal copy order,
held-copy isolation, cancellation, process recovery and real Aspire SDK/MCP flows
require their own original evidence. No source change establishes throughput,
endurance, power-loss durability or three independent write streams.

Related: [ADR-003](ADR-003-durability-ack-barrier.md),
[ADR-136](ADR-136-orleans-partition-ownership.md),
[ADR-143](ADR-143-independent-silo-replica-topology.md),
[ADR-145](ADR-145-no-request-throttling.md), StorageReplication REQ-REP-008.
