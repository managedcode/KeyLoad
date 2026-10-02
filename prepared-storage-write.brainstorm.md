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
