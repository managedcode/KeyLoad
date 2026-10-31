# Query multiline numeric responsibility extraction

AC-CQ-MULTILINE-BLOCKS-001/002/003 preserve the64-method/200-type/400-file limits with executable Allman blocks. REQ-QUERY-001/003 and AC-QUERY-004/007 retain existing read-resource and authority contracts; this structure-only change adds no SQL syntax, public AST, alias, field ID or setting.

The partition-query execution retains its original admission/expiry/linked token scopes, individual preparation, bounded concurrent leaf completion and operation check. Each completed leaf result and its owner are added to the same ordered leaf builder immediately before the same merge/public map. Cancellation and disposal remain with the original owner.

`QueryValidation.Normalize` retains partition/model/structural checks before projection and order normalization, then the same join, predicate and parameter checks. `QuerySelectionNormalization` owns only the existing projection/order clones, path depth/star rules, identifier/null/alias validation and exact error messages. Its depth bound comes from the same validated caller snapshot; it introduces no raw configuration owner or new limit.

```mermaid
flowchart LR
  A[Original authorized query owner] --> B[Same bounded validation]
  B --> C[Extracted shape responsibility]
  C --> D[Same typed plan and ordered result]
```

Existing complete QueryAdapterTests, PartitionQueryContractTests and PartitionQueryAuthorizationTests retain their original declarations/arguments/oracles. Public cluster tests keep real SDK/MCP callers and the Aspire-owned topology.
