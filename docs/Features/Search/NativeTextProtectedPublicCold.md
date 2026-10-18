# Protected public text mutation and persisted FieldUse across cluster cold cycles

REQ-FTS-PUBLIC-PROTECTED-COLD-001 supplements NativeFullTextProjection REQ/AC-FTS-002/004/005/007 and ADR-078/081/125. Use the actual original protected NativeTextClusterScenario and a dedicated keyed ClusterFixture. An acknowledged SDK Patch changes the first document's text and an acknowledged official MCP Delete removes the second. Require complete independently constructed redacted RankedDocument pages, literal references/revisions/rank1/61, and empty original/deleted terms through both clients. Retain the actual two CommandRequests and full CommitReceipts; replay each original through SDK and official MCP without filtering or restamping any result fields.

AC-FTS-PUBLIC-PROTECTED-COLD-001 maps NativeTextProtectedPublicColdTests.ProtectedPatchDeleteAndFieldUseRepairPreservePublicPagesAndOriginalReceiptsAcrossTwoClusterColdCycles (one Test, no Arguments). Assert current pages BEFORE and AFTER replay, so replay cannot conceal a stale or changed projection. Preserve complete receipt identity and ProcessDurable, including original mutation revisions and token.

REQ-FTS-PUBLIC-PROTECTED-COLD-002: persist actual reader FieldUse removal at literal epoch2. Require dispatched PermissionDenied through SDK and official MCP while the credential stays valid; compare complete public OutboxStatus before/after those reads. Close original client owners, kill all three actual Aspire-managed nodes, begin all restarts and join all three original RestartContainerAsync calls. Fresh client sessions after first cold must still deny. Restore the same original persisted reader grants at literal epoch3, require complete configured PrincipalRecord equality, then real WaitForIndex and literal search pages. A second full three-node cold cycle must retain repaired policy, exact pages and both original complete receipts. No role supplied by callers, raw storage/projection deletion, fake provider, timeout increase or new configuration.

AC-FTS-PUBLIC-PROTECTED-COLD-002 maps the same complete case: warm epoch2 denial, first-cold epoch2 denial, epoch3 repair and second-cold healthy pages/replay. Real public wait is an acknowledged prefix, not a fabricated local cut or node-local owner proof. Public outbox equality is not full canonical or raw journal no-effects evidence. No forced index reset/rebuild is claimed: actual process restart/reopen and unchanged native search decide their supported publication/reconstruction path.

REQ-FTS-PUBLIC-PROTECTED-COLD-003 / AC-FTS-PUBLIC-PROTECTED-COLD-003: reuse original RequestCqrsClusterCallers.RunOwnedAsync so original MCP/HTTP sessions join before each kill and original body/disposal failures are preserved. Original test deadline and cancellation remain unchanged. Dedicated original fixture owns Aspire resources and shutdown. No power-loss/endurance/performance result follows from these cold cycles.

```mermaid
flowchart LR
 Seed[Original protected persisted seed] --> Mutate[SDK patch and MCP delete]
 Mutate --> Pages[Full literal pages and receipt replay]
 Pages --> Deny[Persisted epoch2 FieldUse denial]
 Deny --> Cold1[Close callers and all3 cold restart]
 Cold1 --> Deny1[Fresh callers still denied]
 Deny1 --> Repair[Same persisted subject epoch3 and prefix wait]
 Repair --> Cold2[Full pages/replay and second all3 cold restart]
 Cold2 --> Healthy[Fresh callers/full pages/original receipts]
```

Canonical map: Integration Search Cases/NativeTextProtectedPublicColdTests; Models/NativeTextProtectedPublicColdState; Helpers/NativeTextProtectedPublicColdTrial, NativeTextProtectedPublicColdMutation, NativeTextProtectedPublicColdRestart; Assertions/NativeTextProtectedPublicColdAssertions. Product/SDK/MCP/shared fixture changes N/A. Existing NativeTextClusterTests covers warm redaction/update/delete; NativeTextAsyncClusterTests covers warm FieldUse removal; NativeTextWaitClusterTests covers prefix/delete. This adds their missing joined cold/repair/full receipt composition and does not replace original cases. The native kernel full-text proof remains distinct and supplemental.
