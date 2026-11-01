# ADR-058: Native storage caches and disposable derived memory

Status: Accepted; current native storage design under ADR-011.
Related: [ResourceExecution](../Features/ResourceExecution.md),
[NativeMvcc](../Features/StorageRecovery/NativeMvcc.md), ADR-022 and ADR-035.

## Decision

ZoneTree owns physical storage caches, snapshot visibility, native version retention
and cache cleanup. Each physical tree uses its public `CreateMaintainer()` API.
KeyLoad has no additional point-value cache, cache read permit, per-store cache
configuration or Orleans cache-control capability. It schedules no second engine
cleanup timer and adds no writer semaphore or reader/writer lock around database
operations.

The common retained-memory budget belongs to disposable derived indexes and other
real consumers that own retained memory. It never grants data access or replaces
canonical storage. Orleans owns activation scheduling and clustering. Shared-service
shutdown still joins actual borrowers before closing physical resources.

Every operation checks fresh persisted authorization. Native cached bytes are
physical records, never authorization decisions or reusable authorized documents.
[NativePolicyEpochQualification](../Features/ResourceExecution/NativePolicyEpochQualification.md)
keeps the classification, revocation, redaction, cancellation, replay and cold-open
acceptance. Native snapshots preserve transactional read cuts; plain tables expose
their explicit non-transactional semantics.

## Verification

Real-store native MVCC, retained-reader disposal, authorization, two-reopen,
backup and process-recovery operations verify these contracts. Exact-source Linux
SDK/MCP, coverage, endurance and release gates remain distinct. No throughput,
allocation, physical-I/O or RSS improvement follows from removing duplicated
infrastructure without matched measurements.
