# Native reads and current policy epochs

[Authorization](../Authorization.md) and [ResourceExecution](../ResourceExecution.md)
require fresh persisted policy for every operation. ZoneTree owns physical record
caches; KeyLoad does not cache authorized documents or add a point-value cache.

`NativePolicyEpochTests.NativeReadsRespectResourceClassificationAndCurrentPolicyRepairThenTwoReopens`
implements REQ-AUTH-NATIVE-POLICY-001 / AC-AUTH-NATIVE-POLICY-001 using a real store and database engine. An initially unclassified document is fully
visible. A persisted resource-policy change classifies its secret field; the same
reader receives the exact redacted document. Subsequent principal policy epochs
restore raw-field access, withdraw scope access, and restore access again.
The denied operation preserves records and position. Caller cancellation preserves
the original cancellation token; a healthy read follows. A public-field patch
preserves the hidden bytes, and matching seed and patch retries preserve their
original receipts and stored outcomes.

Two joined close/reopen transitions preserve persisted identity, signing bytes,
policy, complete records, position and replay behavior. The fixture registers
physical owner settlement before opening and never reopens or deletes a directory
whose original close has not returned. Body and cleanup failures remain observable.

These are local authorization and persistence cases. Replicated SDK/MCP,
activation movement, endurance and exact-source Linux qualification remain separate
gates. This operation makes no throughput or memory-saving claim.
