# Public chunked BlobStorage brainstorm

Goal: fulfill the owner's required persisted binary uploads and partial reads
through the same authorized Orleans database, .NET API and official MCP tools.
Private replica snapshot pieces and backup archives remain separate capabilities.
Read root/local policy, Architecture, BlobStorage and Proposed ADR038 first.

The product needs one canonical feature, bounded typed parts/ranges, persisted
current principal and resource authority, stable command retry identity, atomic
complete-object visibility, revision-bound reads and safe reclamation. Every
customer-visible command must use the existing canonical ordered apply; files,
locks and transactions stay in node-local PartitionHost. New storage providers
or duplicate ManagedCode implementation cannot hide a dependency defect.

Candidate A stores bounded binary parts and versioned manifests as native atomic
key/value records in the existing node-local provider. Ordered part commands keep
progress and integrity state, publish a manifest with revision CAS, and clean up
staged/retired objects through bounded replicated operations. A short range visits
only its required chunks under the existing scoped read gate. This favors replay,
replication, backup and one authority/transaction implementation, but requires
explicit persisted aggregate quotas, caller retries and bounded reclamation.

Candidate B stores separate files/object-provider bytes and canonical metadata.
It can reuse ManagedCode.Storage, but cross-storage atomicity, staged durability,
replica recovery and orphan authority introduce additional failure boundaries.
Accept it only if actual existing owned source supplies those guarantees.

Candidate C returns complete blobs or simply renames snapshot/backup chunks is
rejected: it fails bounded partial reads or public identity/auth/lifecycle.

Provisional direction: investigate Candidate A against the actual read-only CLR
contracts, authorization, transaction/replay, storage/read gates and admission.
64KiB parts, up to1GiB and an explicit rolling integrity chain are initial design
inputs, not accepted API/limits. Resolve publication replacement, read consistency,
reservation/reclaim quotas, crash cuts, missing/corrupt chunks and downgrade safety
before acceptance, ADR approval or runtime implementation. Standard whole-file
SHA256 must not be promised by an O(1) chain without an actual valid algorithm.

Parallel research task graph before writes:

| Task | ACs / owner / permissions | Start and artifacts / join |
|---|---|---|
| TASK-BLOB-CONTRACT-RESEARCH | AC-BLOB-001–004; high-capability native storage reviewer; read-only source and primary upstream docs, no builds/tests/qualification | This framing and current Proposed ADR038; exact available transaction/auth/admission/codec APIs, conflicts, bounded quota/reclaim/publication options and proposed disjoint ownership; root reviews then writes detailed acceptance/plan and accepted ADR before runtime tasks |

Runtime baseline is GitHub Actions only after a concrete source snapshot exists.
Development compile/static source review is not passing lifecycle, cancellation,
recovery, RF3, resource metrics, coverage, power-loss or endurance evidence.

## Accepted source-review corrections

Native review found two additional bounds and authority decisions before rollout:
record-count-only restore pages can exceed the journal frame, and rechecking a
later head ACL on retained upload cleanup can strand the creator's reservation.
Use existing bounded owner/project identifier validation plus a16384-byte private
head/state record ceiling and byte-bounded metadata collection. Preserve the
current head check for Begin/Complete/Delete, while Write/Abort/UploadInfo/Reclaim
check their selected persisted row and creator. Actual head linkage still rejects
current-version reclaim. The exact accepted contracts are ADR-038/ADR-011; source,
tests-first and runtime evidence remain distinct gates.
