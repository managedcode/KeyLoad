# Native discovery file admission responsibilities

Canonical slice: ClusterReplication. Existing private-files, topology, immutable-record, snapshot, signed-purpose and RF3 contracts remain unchanged.

A feature-local filesystem helper owns original file-lock admission, regular/private-file measurement and allowed-name validation. The existing owner retains its synchronization lock, immutable dictionary, snapshot census, voters/session, JSON readers and every record write. The helper receives the same readonly root/options references and original owner Measure/Snapshot callbacks. It executes the original platform check, directory check, optional measurement, exclusive file open, snapshot, action and disposal in the same order. Option fields are read at their original positions, not sampled as scalar arguments.

No lock, callback, fsync, scope, count, private mode or error-code weakening is permitted. Original setup/negative/refusal/cleanup behavior and combined compiler, process recovery and Aspire RF3 gates remain mandatory and unqualified by source review.
