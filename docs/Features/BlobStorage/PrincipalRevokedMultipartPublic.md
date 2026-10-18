# Persisted principal revocation across public multipart reads and cold ownership

Canonical BlobStorage REQ/AC-BLOB-002/003/004. Acceptance code qualification is pending. This whole-flow differs from warm BlobRead scope removal and API-key revocation: the persisted PRINCIPAL is revoked while the unchanged key and grants remain.

REQ-BLOB-PRINCIPAL-001 / AC-BLOB-PRINCIPAL-001: publish two genuine parts (65536+17 bytes) with independently fixed byte(position)=(position*13+5)%241, literal raw-part SHA256 and independent 72-byte integrity chain from authentic Begin seed. Full original Begin/Parts/Complete receipts are independently bound (command/partition/quorum/positive position/EMPTY mutations), complete metadata and current upload. A persisted BlobRead principal at epoch1 reads full typed metadata, first4, boundary65534/count6, tail65536/count17 and EOF65553/count0 through both genuine public sessions.

REQ-BLOB-PRINCIPAL-002 / AC-BLOB-PRINCIPAL-002: persist the same COMPLETE principal with Revoked=true and PolicyEpoch2, preserving original grants/owner/key. The original live SDK metadata and each legal range return full independently native Unauthenticated Problem/no value; each original officialMCP operation fails HTTP401. Each individual result is immediately followed by independent administrator full metadata/range/upload/list/outbox observation. These are public current observations, not one raw canonical/journal cut or no-append evidence.

REQ-BLOB-PRINCIPAL-003 / AC-BLOB-PRINCIPAL-003: join original callers, then genuine all-three cold restart. A fresh owned SDK still refuses every metadata/range request. Actual officialSDK initialization with that key must fail HTTP401. This is TRANSPORT AUTHENTICATION ADMISSION refusal, not a fabricated range-tool dispatch or typed MCP error envelope; no claim of executing a range operation before initialization. Unexpected successful initialization must be disposed and must fail the test. Observe complete independent administrator state/outbox after that admission refusal.

REQ-BLOB-PRINCIPAL-004 / AC-BLOB-PRINCIPAL-004: persist full original principal with Revoked=false and PolicyEpoch3, then fresh SDK/officialMCP callers must read all independently literal full pages. Replay each original administrator publication DTO through EACH route followed immediately by full current observer. Join callers before a second genuine all-three cold cycle; fresh callers retain identical bytes/metadata/full original results and current authorization. Original deadline and ordered body/disposal/restart failures remain; no clocks, sleeps, fallback, provider, library or API change.

Canonical roles: existing BlobReadPolicyPublicTests Case retains original Trial first; new BlobStorage Models/BlobPrincipalRevokedR44State, Helpers/BlobPrincipalRevokedR44{Corpus,Seed,Publication,Trial,Policy,Replay}, Assertions/BlobPrincipalRevokedR44{Pages,Refusals}. UI/product/contracts N/A; acceptance-only original API composition. Original published time/Begin expiry/seed are authentic retained opaque producer metadata, not predicted caller time. Complete literal content and receipt authority are independent; current server outcome bytes are never normalized.

```mermaid
flowchart LR
 A[Independent two-part publication and reader healthy] --> R[Same principal revoked epoch2]
 R --> D[Each live route HTTP authentication refusal and current observation]
 D --> J[Join callers then all3 cold]
 J --> I[SDK refuses and official SDK initialization401]
 I --> P[Full principal repaired epoch3]
 P --> H[Fresh full literal reads and each original receipt replay]
 H --> C[Join callers second all3 cold and full healthy controls]
```

Coverage distinction: PublicReadPolicy scope removal is warm-only; BlobRangePolicyCold is native kernel-only; CredentialAuthorityFlow changes API-key revocation/expiry and repairs before cold. No existing public multipart principal-revoked cold flow is reused as qualification. Compiler, original UID, runtime and Linux cluster remain pending.

Full receipt mutation vectors are independently EMPTY under the native BlobStorageOperations.Result constructor. Exact SDK credential denial detail is `The credential is unavailable or expired.`: owning credential validation throws Unauthenticated; original node executor forwards reply.Error/SafeDetail; HTTP middleware constructs the canonical full Errors.Problem. Warm MCP operations and cold Initialize refusals are HTTP authentication admission errors, not a typed tool Problem.

The implementation preserves the complete administrator current observer after every individual reader/replay response (14 public calls: metadata2, full legal chunks4, upload2, list2, outbox2). These repeated controls remain bounded by the original test-owned cancellation token; completion within that original budget is not yet qualified. No new timeout. New type absence, existing Case native checkOnly, whole new creation checkOnly, compiler and Linux cluster execution remain pending under the closed native boundary.
