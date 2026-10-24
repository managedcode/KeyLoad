# ADR-146: Group commit

Status: Accepted by owner direction 2026-10-24 ("fix KeyLoad's write path first"). In source; Linux qualification pending.

## Context

Every acknowledged canonical command paid for one journal flush on every copy. The
database grain was non-reentrant, so one command at a time ran its preparation, its
apply on each owner and that owner's `Flush(true)`. On macOS one 1 KiB journal
append with its flush takes about 4.1 ms and one complete embedded commit about
5.0 ms (`JournalFlushBenchmarks` and `CommitThroughputBenchmarks`, 2026-10-24).
Sixteen concurrent writers did not change the per-commit time (4.6 ms), because
each commit still waited for its own flush. The Linux benchmark cells measured
about 265 acknowledged writes per second. Durability must stay equal to the other
databases in the comparison, so the flush itself stays.

## Decision

Concurrent canonical commands share one commit and one journal flush per copy.

- `DatabaseCommitGrain` is `[Reentrant]`. `ApplyAsync` queues the command and
  returns when its batch decides it. One drain at a time takes every queued
  command, in submission order, as one batch. Commands that arrive while a batch
  persists form the next batch. Nothing limits the queue or the batch; the shared
  journal frame decides how many commands one commit holds.
- `ISiloDatabaseService.PrepareBatchAsync` prepares the batch at the owner with the
  highest cut, the same owner as before. For a command that already committed it
  returns the retained original, as `ReadStoredCommandAsync` did. Business time never
  decreases along a batch. Preparation reads the committed state, which does not yet
  hold the effects of the earlier commands in the batch. So the prepared prefix stops
  before a later command that repeats an identity already in the batch, whose
  preparation records queue retry decisions, or whose preparation fails. That command
  is then prepared first in the next batch, exactly as it would be alone.
- `ISiloDatabaseService.ApplyBatchAsync` applies the prepared commands, in order, as
  one ZoneTree commit and one journal frame on every owner. Each command is one
  action of that commit (`IAtomicActionStaging`):
  - It sees the writes of the actions before it.
  - Its `Reset`, `ValidateCommit`, candidate validation and frame budget cover only
    its own writes.
  - It takes the next canonical sequence when it persists a new outcome. A retry
    of a command earlier in the batch replays that outcome and takes no sequence.

  A later command does not run in the batch if it would overflow the shared frame,
  or if it fails outside its own persisted outcome. It then runs first in a later
  commit. The first command of a batch always runs, so it behaves as a commit of its
  own.
- The grain acknowledges each command only after every owner returned the same
  sequence and result for it. It then advances the cached cut once for the batch.
  If the copies disagree from some command on, that command and every later one in
  the batch fail with `UnknownWriteOutcome`, and the owners' cuts are read again.
  The rules for a failed batch:
  - A transport failure, cancellation, or an apply that some copy acknowledged
    makes every command the copies received uncertain.
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

- With many concurrent writers, one flush on each copy acknowledges many commands.
  The latency of a single write is unchanged: its batch holds only that command.
- Durability is unchanged. Every acknowledged command is in a flushed journal frame
  on every copy before its caller sees success, and a frame either replays whole or
  not at all.
- Readers wait only for a batch's apply, as they waited for one command's apply.
- The journal format is unchanged: a batch is one ordinary frame. The `last-batch`
  row is a new system row, written only by commits that persist several outcomes.
- Interleaved reads (`ReadCutAsync`, `GetReadOwnerAsync`, background reads) never
  replace the owners a batch is using. A cut read that a batch overtook is not
  cached.

Related: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-143](ADR-143-independent-silo-replica-topology.md), [ADR-145](ADR-145-no-request-throttling.md), StorageReplication REQ-REP-008.
