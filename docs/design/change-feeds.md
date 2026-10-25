# Committed outbox, document changes and scalar live queries

The system outbox is a logical mutation stream inside one atomic partition. It is separate from the private redo journal, business event streams, retained topics and queue delivery. Each entry stores a monotonically increasing sequence, its command-local ordinal, commit token, evaluation time, accepted mutation and receipt. Document mutations also retain canonical before/after images. The outbox entry, strict indexes, domain effect, command outcome and apply watermark share one compiled redo frame. Failed batches publish no entries; a same-command retry does not append again.

Documents written before a consumer starts are included through a live-query snapshot; the outbox never synthesizes past history. Entries and consumer checkpoints are canonical backup state and are copied with every other write to the `ReplicaCount` ZoneTree owners. Reopening and compaction preserve them; administrative restore changes the incarnation and invalidates old tokens.

## System projection lifecycle

These APIs require a current cluster administrator. Raw mutations and historical images are confined to this boundary; public feeds expose only authorized projections.

| SDK/API | Contract |
| --- | --- |
| `ConfigureProjectionAsync` / `admin/projections/configure` | Create an immutable named consumer/index generation with resource and mutation-kind filters. An empty filter accepts every resource/kind. `StartAfter` must be a retained cut; the default starts at the retained beginning. Register a rebuild consumer before reclaiming its required tail. |
| `ReadProjectionAsync` / `admin/projections/read` | Read from its persisted checkpoint and issue a signed contiguous batch. Filtered positions advance the batch's through-position. No checkpoint changes until commit. |
| `CommitProjectionAsync` / `admin/projections/commit` | Apply canonical effects within the same atomic partition and persist the checkpoint and replay receipt together. The batch token binds incarnation, consumer, generation and exact start/end positions. An empty tail batch cannot produce effects. |
| `ReleaseProjectionAsync` / `admin/projections/release` | Release that generation's retention pin and fence old tokens. Use a new consumer identity for a new filter, generation or rebuild. |
| `OutboxStatusAsync` / `admin/outbox/status` | Inspect the retained range, byte/record counters and consumer checkpoints. |
| `PurgeOutboxAsync` / `admin/outbox/purge` | Reclaim a bounded contiguous prefix, at most through the lowest active checkpoint. Released consumers no longer pin history. |

Effects failing a CAS, permission or quota check roll back both effects and checkpoint. A different command retrying a completed batch with the same effects returns its original receipt and `AlreadyProcessed`; changed effects conflict. Current generation and effect permissions are checked before returning a cached result. A concurrent batch based on a checkpoint which has advanced must reread. Checkpoints cannot be supplied as arbitrary unauthenticated offsets.

The outbox has no producer quota or progress reserve ([ADR-145](../ADR/ADR-145-no-request-throttling.md)): every accepted producer appends its entries, and retained size depends only on consumer progress and purge. An atomic partition has no projection consumer count limit ([ADR-149](../ADR/ADR-149-no-operation-limits.md)). A projection batch's effects, checkpoint and replay receipt commit together. A filtered batch with no effects can always advance its checkpoint without adding outbox payload.

This lets pinned consumers process a full producer outbox and advance the cleanup boundary. `PurgeOutbox` advances the retained prefix; it still cannot cross an active consumer checkpoint. The atomic frame limit applies. Consumers should exclude derived targets from their filters to avoid feedback; amplifying or cyclic projections can still reach the hard ceiling. Retention cleanup is currently explicit, so an operator must process consumers and purge the shared completed prefix. Automatic retention, expired receipts/generation cleanup and external-file atomic manifests remain qualification work.

Projection read budgets default to 100 examined entries and 4 MiB of selected entry payload; the caller may choose any positive values, and the server sets no maximum. Each immutable entry is loaded separately and an undelivered entry remains after the returned through-position. A first entry larger than the caller's byte budget returns `BudgetExceeded`.

External files must use a replay-safe journal or atomic generation manifest before advancing this checkpoint. An HTTP checkpoint commit alone does not establish external-file durability. The current atomic effects path demonstrates canonical document projections, rather than claiming a qualified ANN/FTS provider lifecycle.

## Protected document feed

`POST /changes/read` (`ReadChangesAsync`) requires `ChangesRead | DocumentsRead` on its collection. `Beginning` starts at the currently retained first position; `Now` captures the current tail. Every page, including an empty tail, returns a signed cursor. This profile polls bounded pages and creates no server subscription, lease or public retention pin.

A returned change contains the sequence/commit, entity reference, revision, deletion flag and safely projected before/after images. Before images require their historical row ACL; all images also require current row visibility and the after-image ACL. Deleted after images carry metadata and no document payload. Sensitive field classification uses the same secure projector as ordinary document reads. No raw outbox payload or unrelated resource identity is returned.

Positions are shared by mutations in the atomic partition, so filtered sequences can contain gaps in the public output. `Limit` bounds examined positions, rather than promising that many visible changes. Byte limits bound the selected change payloads. Empty filtered pages with `HasMore` must still be continued. Saving a cursor after applying its page supports at-least-once reconnect within the retained window; saving it first can lose application delivery. Consumers apply ID/revision updates idempotently.

The cursor binds incarnation, partition, collection, principal, policy epoch, schema and row-visibility epoch. A document ACL change advances that epoch in the same atomic write and rejects old cursors with `TokenInvalidated`, requiring a new authorized snapshot. Reauthorization precedes reading. Principal revocation rejects the call; a policy update fences old cursors. Prefix reclamation past the cursor yields `HistoryUnavailable`. Both conditions require clearing the old projection and resynchronizing. Cursors expire after 24 hours and can continue on any caught-up copy of the same cluster.

## Scalar live-query profile

`POST /query/live/start` captures the complete initial Q1 result and outbox tail under one storage read gate. It requires query/document/change-read grants and ordinary predicate field-use grants. The query must be unordered, scalar and read-only, with no query continuation or `EXPLAIN`. A result larger than `Limit` or its byte budget fails rather than returning an incomplete subscription baseline.

`POST /query/live/read` binds the same normalized AST and evaluates each authorized before/after image with the ordinary Q1 evaluator. A matching after image emits `Upsert` using the ordinary query projector; an image leaving the predicate or being deleted emits `Remove` without a payload. Nonmatching changes still advance the cursor. Protected values can be used by a granted predicate while remaining omitted in returned rows.

The profile exposes a complete bounded initial snapshot and bounded delta pages. It retains no server result-set state; the application bounds its maintained set and polling rate. `ORDER BY`, top-k, aggregates, joins, graph and ANN subscriptions need separate cost/freshness profiles. Live-query calls share ordinary query admission, validation, deadline, candidate-byte and field-lineage controls. Starting at a different predicate/projection or changing policy/row visibility invalidates the cursor. Query snapshot plus tail removes the initial subscription race; it does not provide indefinite history retention or cross-partition coverage.

Unit tests compare seeded live deltas with ordinary Q1 results, exercise concurrent snapshot/write, protected input grants, row/policy fencing, byte boundaries and replay. Real process-kill tests cover projection effects/outbox/receipt/outcome/checkpoint publication. Power-loss, external manifests, multi-shard coverage, replicated-cluster continuation after node loss and endurance remain distinct qualification gates.
