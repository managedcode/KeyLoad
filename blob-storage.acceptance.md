# BlobStorage acceptance

Contract owner: KeyLoad integrator. ADR: [ADR-038](docs/ADR/ADR-038-chunked-blob-storage.md).
Specification: [BlobStorage](docs/Features/BlobStorage.md). Source/test work is
authorized; every runtime criterion below is pending until exact-SHA GitHub Actions
evidence exists. Native files, transactions, process recovery and RF3 clients are
required; doubles and implementation-only assertions do not qualify.

| Criterion | Observable pass/fail condition | Required proof / task |
|---|---|---|
| AC-BLOB-001 | Empty, one-part and multipart ordered uploads become visible only after successful complete. Same command retry has the same token/effects; same ordinal with identical bytes/hash is not charged twice; gaps, changed duplicate, wrong hash, short nonfinal part, excess bytes, premature complete and wrong complete chain fail without publishing. Concurrent CAS uploads have one winner. | Real ZoneTree lifecycle and reopen tests; RF3 .NET/MCP upload parity; TASK-BLOB-TESTS, ENGINE, ROUTING, CALLERS |
| AC-BLOB-002 | First, boundary-crossing, interior, final and zero-length ranges at one positive expected revision return exact bytes, offset and metadata. Negative/overflow/out-of-bounds/count>65536 fail; missing/deleted is NotFound; stale revision is RevisionConflict. At most two chunks are visited, each charged before decode/copy. Corrupt/missing part yields Corruption without partial response. Cancellation releases the scoped gate/budget; healthy follow-up succeeds. | Real provider range/corruption/budget tests and actual HTTP/native MCP callers; TASK-BLOB-TESTS, ENGINE, CALLERS |
| AC-BLOB-003 | Every operation uses the current persisted principal, capability, tenant/database/resource/domain and applicable RowAccess. Another nonadmin principal cannot use an upload ID. Revoked/expired credentials, changed PolicyEpoch, forged roles, wrong scope and forbidden management do not leak bytes or mutate state. Protected cluster-membership identity cannot invoke blobs. | Actual persisted security and official RF3 SDK/MCP failures; TASK-BLOB-TESTS, AUTHORIZATION, CALLERS |
| AC-BLOB-004 | Part data/meta/state/quota/outcome/watermark share the canonical commit. Publication and delete use head revision CAS. Reopen/process-kill and RF3 leader-loss retry recover exact authorized results. Active unexpired/current versions cannot be reclaimed. Bounded reclaim deletes only the selected expired/aborted/retired version and releases bytes only with actual logical part deletion; reads inside the store gate remain valid during cleanup. | Real files, CrashHost cuts, native replica restart/rejoin; TASK-BLOB-TESTS, ENGINE, RECOVERY, CALLERS |
| AC-BLOB-005 | Resource and store-wide reserved bytes, object-key/version/upload counts are persisted and checked atomically before accepting a begin. Defaults and hard ceilings are measurable. A second resource cannot escape global quota; abort/expiry release unused reservation once; overwritten bytes stay charged until reclaimed. Missing/negative/inconsistent counters or unknown format fail closed. Initial catalog proof is finite10000 delivered records under native examined-work limits: existing BlobStore evidence is Corruption, unfinished proof is BudgetExceeded, and neither initializes counters. | Small configured real-store boundary/concurrent/reopen/adversarial tests; TASK-BLOB-TESTS, ENGINE, AUTHORIZATION |
| AC-BLOB-006 | Metadata, upload state and ordered authorized listing are bounded and contain no full payload. Listing scans <=2*limit candidates (limit1..100), skips rows the principal cannot read and advances an exclusive AfterId from the last visited row. Real .NET and MCP expose exactly the same ten operations and canonical error/request-ID/commit-ID semantics. | Exact schema/DTO goldens, actual provider listing and RF3 official MCP discovery/operation catalog; TASK-BLOB-CONTRACTS, TESTS, ROUTING, CALLERS |
| AC-BLOB-007 | Per-part SHA256 and sha256-chain-v1 are distinguished from whole-file SHA256. The chain binds store incarnation, full scope, upload, declared length and 65536-byte layout; complete compares the caller's expected chain. Existing nonblob resource JSON/fingerprint and enum numeric values stay unchanged. Unsupported format/downgrade cannot silently reinterpret blob data. | Golden byte/JSON/enum vectors, real-provider unknown-format checks, documented downgrade boundary; TASK-BLOB-CONTRACTS, TESTS, ENGINE |

AC-BLOB-003/005 additionally require the exact applicable-row authority matrix
and256-byte UTF-8 optional owner/project identifiers in ADR-038. Private head/state
records are bounded at16384 encoded bytes. AC-BLOB-004 requires resumable restore
pages bounded at128 records and a4MiB normal byte budget; a single larger native
catalog record is processed alone without rewriting its payload. Genuine tests
cover invalid metadata without effects, retained-version cleanup after a head ACL
change, current-version protection and persisted grant revocation.

All REQ-BLOB-001–007 map one-to-one to these criteria in the durable feature spec.
Formatting, complete solution build/analyzers, source complexity, all TUnit suites,
real recovery, RF3, changed-code coverage and faults/endurance remain required.
Logical quota is not physical disk usage or an unlimited-size snapshot guarantee.
