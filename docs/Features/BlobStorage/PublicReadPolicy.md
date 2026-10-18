# Public blob read policy on original caller sessions

This slice refines REQ/AC-BLOB-003 with genuine resource-scoped credentials. Existing kernel BlobAuthorizationTests and BlobRangePolicyColdTests remain unchanged; document discovery-policy tests do not establish blob metadata or zero-range authority.

REQ-BLOB-PUBLIC-POLICY-001 requires each metadata and range call to reload actual persisted BlobRead scope. AC-BLOB-PUBLIC-POLICY-001 maps to BlobReadPolicyPublicTests.PersistedReadGrantRemovalAndRepairControlMetadataAndEveryRangeOnOriginalPublicSessions (one Test, no Arguments): real root Begin/Part/Complete publication, separate scoped persisted credential, exact current metadata and literal bytes [11,23,37,53] plus exact-end zero range through SDK and official MCP.

REQ-BLOB-PUBLIC-POLICY-002 preserves current authority and repair. AC-BLOB-PUBLIC-POLICY-002 requires actual ConfigurePrincipal epoch2 Grants=[] with full returned DTO equality; original reader sessions repeat literal PermissionDenied metadata/nonempty/endzero, null SDK value and authentic dispatched MCP request. After EACH refused request pair an independent administrator reads full healthy metadata/ranges and complete public outbox equality against the POST-policy baseline. Epoch3 actual full-principal repair restores the same reader's original bytes immediately; complete original root publication results/receipts replay through both clients and independent healthy values follow replay.

REQ-BLOB-PUBLIC-POLICY-003 bounds ownership. AC-BLOB-PUBLIC-POLICY-003 uses original keyed ClusterFixture, persisted key, discovered Aspire cluster endpoints, original RunOwnedAsync body/disposal ordered ledger and original TUnit cancellation. No fake roles, protocol, transport, callback, quota or provider is introduced. Public metadata/range/outbox evidence is not an atomic cross-call cut or raw canonical/journal no-effects proof. No cold, mid-copy cancellation, throughput or durability qualification is inferred.

```mermaid
flowchart LR
 P[Actual root publication and scoped reader] --> H[Literal metadata and ranges]
 H --> D[Persist epoch2 no read grant]
 D --> R[Repeated SDK and MCP refusals]
 R --> A[Admin healthy after each refusal]
 A --> F[Persist epoch3 repair]
 F --> E[Same reader healthy and original root receipts replay]
 E --> J[Joined original caller disposal]
```

Canonical map: Integration BlobStorage Cases/BlobReadPolicyPublicTests, Helpers/BlobReadPolicyPublicPublication and BlobReadPolicyPublicTrial, Assertions/BlobReadPolicyPublicAssertions. Existing same-slice BlobSupersessionPublicTrial.Version and BlobSupersessionPublicAssertions.ReplayAsync supply the retained original typed publication/replay contract. No new product source. Source proposal only; source Y, compiler, current census/UID and actual Linux Aspire cluster execution remain OPEN.
