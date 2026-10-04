# ADR-091: epoch7 interpretation fence

Status: Accepted, 2026-10-04. Owner: root integration; implementation worker:
Luna query_wave. Related tasks KL-043/019/094/097/100 and
[REQ/AC-EPOCH7-001 through006](../Features/StorageRecovery/Epoch7.md).

New canonical transfer, saga and projection-lineage records must never be read
by an engine that can ignore their interpretation. RPC fencing alone cannot
prevent offline rollback from exposing protected derived vectors. Increment the
strict data epoch to7 and native checkpoint to5; admit exactly the new serving
format. Preserve stable native contract aliases/IDs and WAL4 framing.

Choose one explicit stopped-copy converter with exact migration-only native5
and native6 profiles and a native7 writer. This preserves the existing source5
acceptance obligations without retaining an obsolete serving path or writer.
Reject automatic migration and in-place header changes because they cannot
prove full-node/current-image authority or preserve an independent rollback copy.

The feature specification is the canonical implementation contract: ordered
stages, exact role-folder ownership, root protocol/AppHost join points, source
inventory/receipt revision, immutable prior5/prior6 tests, bounded crash/retry
verification and all-stopped RF3 rollout/rollback. No live conversion is executed
without explicit scope for the particular user stores. Implementation remains
Accepted until mapped real process, SDK/MCP RF3 and exact-source Linux evidence
exists. Missing probe artifacts fail the gate and must not skip tests.

The C1 RF3 correction is TASK-CRS-C1-EPOCH7 in
[NativeCqrsRequestV2](../Features/ClusterRouting/NativeCqrsRequestV2.md). Its fixed
genuine RPC1 baseline is native6, so current readers use the existing explicit
all-three-Prepare-before-Publish stopped-copy upgrade. A separate owned mixed
copy with published native7 node1 and untouched native6 node2/3 is permitted only
as a cross-epoch loss-of-compatible-quorum fault wave; current node1 must fail serving
and settle completely before homogeneous native7 serving. Preserve original
profile/data/image proofs, C1 receipt workload and node identity, then verify
current cold restart and current writes through both real clients. This does not
qualify a same-epoch protocol-only fault or authorize old readers over native7,
rolling conversion, relabelled images, stale rollback or user-store migration.
Two compatible current voters retain the legitimate RF3 majority when the prior
signing purpose cannot authenticate as a current observation. The negative
fixture must not assert unavailable serving for that valid majority or invent
an authenticated mismatch; it uses one current voter and two genuine prior voters.
Root owns the shared preparation seam, docs and all gates; Luna lifecycle_wave
owns only the private feature-local C1 fixture patch and obsolete-helper removal.

```mermaid
flowchart TB
  Contract[New persisted interpretation] --> Data[Strict epoch7 admission]
  Contract --> Peer[Capability3 and signed-purpose fence]
  Prior[Exact old binaries] --> Reject[Old readers reject native7]
  Source[Stopped5 or6 with complete authority] --> Copy[Separate verified7 copy]
  Copy --> RF3[Start only homogeneous verified RF3]
```
