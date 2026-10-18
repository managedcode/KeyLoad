# Maintained native text current reader authority

Persisted RestrictRows OwnerId and RawRead/FieldUse grants are freshly evaluated for every selected query. Changing only the reader principal must not silently turn the generation into a caller-authority cache.

Publish two independent bilingual documents at revision1 with OwnerA/OwnerB RowAccess and a protected /text field, retain full original document receipt and Build result. Reader policy1 OwnerA returns only Ukrainian literal row through SDK and official MCP; policy2 OwnerB returns only English literal row without rebuilding. Policy3 removes RawRead but retains FieldUse: exact English row JSON omits /text, Redacted=true and RedactedFields=[/text], rank1/61. Policy4 removes FieldUse: each actual client refuses PermissionDenied, immediately bracketed by complete administrator selected/current pages and unchanged post-policy public OutboxStatus. Policy5 restores OwnerA/RawRead/FieldUse and complete expected principal equality; exact Ukrainian literal page resumes on the SAME selection.

Close all genuine callers before each of two existing all-three kill/begin/restart cycles; repeat policy4 refusal after first cold, repair5, then repaired pages/replays after second cold. Use the existing Aspire ClusterFixture, SDK and official MCP owners; no index fallback, schema change, new provider, clock, quota or API.

```mermaid
flowchart LR
 A[Admin bilingual Build and full receipts] --> B[Reader1 owner A literal page]
 B --> C[Policy2 owner B literal page]
 C --> D[Policy3 RawRead removed exact redaction]
 D --> E[Policy4 FieldUse removed refusal]
 E --> F[Join callers then all3 cold refusal]
 F --> G[Policy5 owner A full repair]
 G --> H[Original replays bracket current pages and outbox]
 H --> I[Join callers then all3 cold healthy]
```

Canonical slice Search: proposed new Cases/NativeTextAuthorizedQueryR39Tests.cs; Models/NativeTextAuthorizedQueryR39State.cs; Helpers/NativeTextAuthorizedQueryR39Seed.cs, Policy.cs, Replay.cs, Trial.cs; Assertions/NativeTextAuthorizedQueryR39Pages.cs. Existing selected Explain/AllowedIds/released repair remain unchanged. Existing unselected RowOwnership changes document Access; RawRead/ProtectedCold query disposable current projection; selected Unit CallerFlow only tests revoked authentication/wrong-generation precedence. This scope isolates unchanged generation with current persisted reader row/field authorization and full genuine public/cold lifecycle. No library adoption.

All query pages retain exact reference/partition/collection/id/revision/JSON/redaction/omitted fields/rank/explanation literals. Each reader response immediately checks complete administrator selected/current bilingual pages and outbox; each administrator read checks outbox. Native selected manifest uses the unchanged builder administrator; policy-only reader changes require no generation Restore/Build. Source read availability is pending during Main Building; no inherited NF/creation readiness is claimed.
