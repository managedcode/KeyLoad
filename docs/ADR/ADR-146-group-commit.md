# ADR-146: Group commit

Status: Accepted. Current source; exact-source distributed qualification remains a separate gate.

## Context

Concurrent commands can share the physical transaction work on every required
copy while retaining their individual outcomes and canonical order. A public
document batch is one command containing several mutations; the commit queue can
combine several such commands into one physical transaction per copy.

## Decision

Concurrent canonical commands share one native process-persistent commit per copy.
The acknowledgement boundary and explicit disk barriers are specified by
[ADR-003](ADR-003-durability-ack-barrier.md); group commit does not add a disk sync
per document or command.

- `DatabaseCommitGrain` is `[Reentrant]`. `ApplyAsync` queues the command and
  returns when its batch decides it. One drain at a time takes every queued
  command, in submission order, as one batch. Commands that arrive while a batch
  persists form the next batch. Nothing limits the queue or the batch.
- `ISiloDatabaseService.PrepareBatchAsync` prepares the batch at a selected owner
  after completing retained gaps to the highest canonical cut. At equal cuts the
  commit grain prefers its selected local copy, avoiding a full-batch remote
  preparation round trip. This changes neither the selected copy set nor the
  requirement for all copies to persist the batch. For a command that already committed it
  returns the retained original, as `ReadStoredCommandAsync` did. Business time never
  decreases along a batch. Preparation reads the committed state, which does not yet
  hold the effects of the earlier commands in the batch. So the prepared prefix stops
  before a later command that repeats an identity already in the batch, whose
  preparation records queue retry decisions, or whose preparation fails. That command
  is then prepared first in the next batch, exactly as it would be alone.
- `ISiloDatabaseService.ApplyBatchAsync` applies the prepared commands, in order, as
  one ZoneTree transaction on every owner. The current `ProcessDurable` path
  publishes through ZoneTree's process-persistent WAL; disk sync remains
  engine-owned periodic work. Explicit `IAtomicStore.SyncAsync` awaits the native
  WAL APIs when a caller requests that disk barrier. Each command is one
  action of that commit (`IAtomicActionStaging`):
  - It sees the writes of the actions before it.
  - Its `Reset` covers only its own writes.
  - It takes the next canonical sequence when it persists a new outcome. A retry
    of a command earlier in the batch replays that outcome and takes no sequence.

  A later command does not run in the batch if it fails outside its own persisted
  outcome. It then runs first in a later commit. The first command of a batch always runs, so it behaves as a commit of its
  own.
- The grain acknowledges each command only after every owner returned the same
  sequence and result for it. It then advances the cached cut once for the batch.
  If the copies disagree from some command on, that command and every later one in
  the batch fail with `UnknownWriteOutcome`, and the owners' cuts are read again.
  The rules for a failed batch:
  - Before the copies are sent the batch to apply (owner selection, completing a
    lagging copy, preparation), no command was applied: a transport failure or an
    uncertain owner fails every command with `OwnershipLost`, so retrying the same
    command is safe.
  - Once the batch was sent, a transport failure, cancellation, or an apply that
    some copy acknowledged makes every command the copies received uncertain.
  - A failure that every copy raised before any effect belongs to the first command.
  - Commands that no copy applied are queued again, ahead of newer commands.
  - A caller that cancels before its command joins a batch sees cancellation. After
    the command was sent, the caller receives `UnknownWriteOutcome`.
- A commit that persisted several outcomes records their outcome keys in the system
  row `last-batch`. `ReadLastCommittedCommandsAsync` returns those commands when the
  row describes the last commit, and otherwise the last command. A copy that missed
  part of the last batch is completed from these commands before the next batch. A
  gap larger than the last batch still returns `RecoveryRequired`.
- One implementation serves both paths. `ApplyStorageOperation` applies a batch of one
  command, and the single-command `ApplyAsync`, `PrepareAsync`,
  `ReadStoredCommandAsync` and `ReadLastCommittedCommandAsync` storage service
  methods are removed. Shared-volume mode keeps one ordinary owner. Its commands
  still commit one at a time inside that owner's invocation.

## Consequences

- With many concurrent writers, one physical commit on each copy acknowledges many commands.
  The latency of a single write is unchanged: its batch holds only that command.
- Every acknowledged command is in a process-persistent ZoneTree transaction on
  every required copy before its caller sees success. Process interruption and
  power-loss persistence require distinct qualification; a grouped commit does
  not strengthen the selected durability contract.
- Readers wait only for a batch's apply, as they waited for one command's apply.
- A batch is one ordinary ZoneTree transaction. The `last-batch` row is a system
  row, written only by commits that persist several outcomes.
- Interleaved reads (`ReadCutAsync`, `GetReadOwnerAsync`, background reads) never
  replace the owners a batch is using. A cut read that a batch overtook is not
  cached.

Related: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-143](ADR-143-independent-silo-replica-topology.md), [ADR-145](ADR-145-no-request-throttling.md), StorageReplication REQ-REP-008.
