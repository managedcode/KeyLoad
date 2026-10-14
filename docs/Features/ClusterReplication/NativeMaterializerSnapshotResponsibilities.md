# Materializer failure and snapshot admission responsibilities

Canonical slice: ClusterReplication. Existing replication acknowledgement, term, authority, atomic apply, checkpoint recovery and RF3 acceptance criteria remain unchanged.

The materializer records either original worker failure under the same signals lock through one private method. Both catch filters, original exception identity, bare rethrow and final waiter wakeup remain unchanged. The physical owner retains all worker, cancellation, apply-gate and shutdown ownership.

Snapshot scope and committed-cut validation use a feature-local stateless helper. The snapshot owner invokes it at the original call sites under the original gate. It reads the same borrowed canonical store and durable log, evaluates the same short-circuit conditions in order, and throws the same error codes. No snapshot/file handle, transaction, authority or lifetime moves to the helper.

This is a responsibility extraction for existing numeric limits, not a protocol or storage-format change. Full solution analyzers, genuine process recovery and Aspire RF3 SDK/MCP gates remain required. Source review does not prove runtime recovery or power-loss durability.
