# Native internal serialization acceptance

Goal: all owned internal typed serialization uses generated Orleans binary contracts, preserving authorized database behavior and real recovery boundaries. Actors are SDK/MCP callers, authenticated peer nodes, node-local storage owners and offline operators. Public API shapes and permissions remain unchanged.

## Scope

Include owned DTO closures; persisted model values/scalars; WAL/checkpoint/identity/backup metadata; replica entries/hard state/snapshot descriptors; Orleans membership and request/reply payloads; signed internal typed claims. Exclude actual public HTTP/MCP JSON, JSON document/query DOM materialization, frozen canonical identity hashes, sortable KeyCodec bytes, native ZoneTree storage framing/raw values, blob bytes, cryptographic algorithms and native Cartograph archives. These are concrete protocols, not alternative internal serializers.

## Criteria and tests

| ID | Pass/fail contract | Automated evidence |
|---|---|---|
| AC-IS-001 | Every owned internal concrete DTO has generated serialization, immutable IDs and stable aliases; inherited fields preserve scope. No runtime JSON record/protocol fallback. | Native contract/type-closure tests and source inventory; enabled full solution compile. |
| AC-IS-002 | Cached native codec uses pooled sessions, exact input consumption and raw byte codecs; malformed/trailing/wrong-version/null-required data fails before effects. | Native codec round trips, corruption/default/empty/null/tombstone and bounded byte tests. |
| AC-IS-003 | Typed model/scalar values and borrowed readers use the same binary codec, preserving atomicity, authority, exact user JSON/raw numeric lexemes and exact quota accounting. | Real ZoneTree model families, queue/topic/outbox accounting and existing mixed-model/schema regressions. |
| AC-IS-004 | Checkpoint, identity and backup have explicit distinguishable new formats, hashes and full validation before install/publication. Unknown/legacy formats fail closed without truncating or recreating data. | Actual-file metadata/checkpoint/backup regressions; process-cut recovery. |
| AC-IS-005 | Replica native payloads preserve sender authentication, control/data replay pools, malformed no-slot effects, exact size limits and opaque-payload allocation contract. Authentic generated record frames, including empty constructor scopes, pass inspection. | Replica transport/security/control tests, generated codec compatibility, native entry batch accounting and RF3 SDK/MCP. |
| AC-IS-006 | Grain requests/replies and membership use attributed native types internally; public JSON is converted only at the API boundary. Cancellation, reply budgets, CAS/ETags and errors remain meaningful. | Grain payload/envelope/request tests, membership regressions and actual RF3 SDK/MCP. |
| AC-IS-007 | Signed typed claims are binary and versioned; frozen operation/idempotency digests remain exact. Old-token policy is explicit and never grants authority. | Signed claims/tamper/scope/version tests and unchanged canonical golden hashes. |
| AC-IS-008 | No legacy store is silently reinterpreted. Offline conversion, if delivered, preserves source/rollback copy, all records, accounting and replica authority with interruption-safe destination publication. Otherwise a precise fail-closed upgrade blocker remains explicit. | Independent legacy fixtures, migration cut tests and exact missing-conversion inventory; no successful-upgrade claim without these. |
| AC-IS-009 | All required gates pass for final exact source; evidence distinguishes compile, native tests, workflow completion, performance and production. | Full native normal/scalar unit, recovery, analyzers and genuine Docker RF3 suites in canonical CI. |

Every requirement maps one-to-one to these criteria in InternalSerialization.md. Native JSON DOM surrogate tests preserve ordered properties/duplicate semantics, numeric lexemes and owned buffers. Semantic validation is required beyond serialization attributes. No large synthetic load tests are added. Performance/power-loss/endurance/production remain unmeasured unless separately evidenced; static review cannot satisfy runtime criteria.

## Assumptions and compatibility

Orleans10.3.1 and existing central package pins are retained. Matching homogeneous cluster versions are required. Existing stopped store rollback copies must be preserved; rollback after new writes requires an independently qualified reverse conversion. No direct Azure or production access. The owner directed source installation, scoped delivery and GitHub qualification on2026-10-03 after the historical automatic-review rejection. Running-store conversion and product release remain outside this source task.

### AC-IS-002 resumption negative and edge inventory

Reject incomplete/duplicate/overfilled array/list counts, incomplete dictionary
pairs, invalid references, excessive native nesting, wrong dynamic root, semantic
cycles and compressed-reference DOM expansion before database effects. Preserve
positive empty collections, supported polymorphism, repeated legitimate references
and public-input null validator precedence. Native wire depth264, semantic/DOM
depth64 and DOM UTF8 output32MiB are explicitly owned structural fences in ADR060;
they do not establish a general decoded-heap bound or measured acceleration.
New native Writer/scalar fixtures and real-store/replay assertions map to AC002/005.

Known typed references to opaque omitted-type unknown fields and unmodeled
System/derived-collection native codec shapes must fail before allocation.
Bounded unknown fields, known-schema references and the actual owned generated
closure retain positive coverage. Full arbitrary native codec compatibility is
out of scope and requires a separately accepted bounded shape contract.

## NSP006 criteria

| ID | Pass/fail contract | Automated evidence |
|---|---|---|
| AC-IS-PERF005 | Warm normalized terminal admission allocates0 auxiliary bytes; nullable admission equals independent unchanged Normalize allocation. No caches or changed limits. | NativeWireSupportedScalarAllocationTests use the no-key TUnit NotInParallel constraint; exact byte assertions,32 warmups/4096 iterations and outside-window data remain unchanged. |
| AC-IS-PERF006 | Supported closed terminal roots pass; open/unsupported roots and unsupported generic-owner arguments fail with the original corruption error. Fixed263 array wrappers pass;264 fail. Generated nullable values/nulls, ordered collections and independent bytes survive round trips. | NativeWireSupportedScalarTests/Fixtures/DepthTests plus the complete existing native normal/scalar suite. |
| AC-IS-PERF007 | All prospective budgets, six exact corpus receipts, full24-cell profile, twelve JSON controls and confidence/cohort conditions in ADR060 hold. Missing/drifted/indeterminate pairs fail qualification. | Actual complete BDN originals and strict report validation; explicit manual arithmetic exception requires source/host/profile-bound originals and independent review. Local two-cell data is insufficient. |
