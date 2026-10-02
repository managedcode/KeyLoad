# Prepared storage write

Goal: remove the evidenced duplicate full mutation projection in a real ZoneTree
commit without changing staged ownership, canonical WAL bytes or fault ordering.
The transaction currently projects sorted staged entries for ValidateCommit's
payload, and the facade projects them again for apply. Caller key/value copies
remain necessary; only the duplicate array and mutation-record projection go away.

Options: a new prepared-commit descriptor adds another allocation and wider
publication changes; streaming serialization changes the durable format path;
a private cached mutation array reuses the existing prepared-payload lifecycle.
Choose the cache. PrepareChanges returns one projection per staged generation;
PreparePayload serializes that array, and the existing facade/journal publisher
consumes it. Accepted Stage and Reset invalidate both caches. A rejected oversized
Stage leaves the last valid generation and its caches intact.

Risks: validation then replacement/delete/reset, caller-array mutation, sorted
apply order, no-op commits, rejected Stage, header/flush/apply fault order and
unknown-write poisoning. Test actual public transactions and independently expected
WAL bytes first; static review must prove one projection, not invent a GC/RSS win.

Independent workstream: one bounded worker owns transaction/facade exact call and
new StorageRecovery regression source. Lead owns contracts/docs and every actual
build/static/GitHub join; other native/query/site owners are protected. Manifest
limits, iterator failure cleanup, checkpoint I/O, new public APIs and format changes
are separate pending scopes.

## Confirmed null-tombstone regression, 2026-10-02

Exact candidate 6949fa0 in GitHub run37005805424 exposes a producer-side adapter
defect after the read-only CLR migration. PrepareChanges implicitly converts a
null byte-array value into a non-null empty ReadOnlyMemory, so the canonical
nullable mutation writes an empty live row instead of a deletion. Fix only that
projection with an explicit nullable null. Replay/apply already distinguishes
null from empty correctly; changing readers to hide empty rows would corrupt valid
empty Put operations. No format/version bump or historical empty-row rewrite is
justified because old empty rows carry no delete provenance.

Keep NullValueOverheadBytes=2: MutationPropertyBytes already includes the two
base64-value quotes; replacing those quotes with the four-byte JSON null adds two
bytes. Test exact frame admission and byte equality, staged/committed borrowed and
owned absence, valid empty Put, WAL replay and snapshot count/reopen. The baseline
already contains real failing frame/delete/typed-read tests; add explicit empty
versus absent regressions before the one-line projection repair. GitHub alone
executes all tests/recovery/RF3 qualifications; a source repair does not recover
historical deletions or prove power-loss durability.
