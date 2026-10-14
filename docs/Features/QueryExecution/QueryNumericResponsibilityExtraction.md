# Query multiline numeric responsibility extraction

AC-CQ-MULTILINE-BLOCKS-001/002/003 preserve the64-method/200-type/400-file limits with executable Allman blocks. REQ-QUERY-001/003 and AC-QUERY-004/007 retain existing read-resource and authority contracts; this structure-only change adds no SQL syntax, public AST, alias, field ID or setting.

`PartitionQueryRemoteExecution.ExecuteAsync` retains its original admission/deadline/linked token scopes, individual preparation, bounded concurrent native leaf completion, revalidation and budget check. The exact final grant counters are copied into the same ordered leaf builder by a private helper immediately before the same merge/public map. Cancellation/deadline translation and disposal remain with the original owner.

`QueryValidation.Normalize` retains partition/model/structural checks before projection and order normalization, then the same join, predicate, parameter and result-byte checks. `QuerySelectionNormalization` owns only the existing projection/order clones, path depth/star rules, identifier/null/alias validation and exact error messages. Its depth bound comes from the same validated caller snapshot; it introduces no raw configuration owner or new limit.

```mermaid
flowchart LR
  A[Original authorized query owner] --> B[Same bounded validation]
  B --> C[Extracted shape or accounting responsibility]
  C --> D[Same typed plan and ordered result]
```

Existing complete QueryAdapterTests, PartitionQueryContractTests, PartitionQueryAuthorizationTests, PartitionQueryPublicBudgetTests, RemotePartitionQueryNativeTests, RemotePartitionDeadlineTests and RemotePartitionParallelCancellationTests retain their original declarations/arguments/oracles. Public RF3 retains RemotePartitionQueryRf3Tests through real SDK/MCP callers and owned topology. Source/native preview/self-review is distinct from common formatter/Release, original whole operations and exact-source Linux qualification, which remain OPEN. No runtime test, speed or power-loss claim is made here.
