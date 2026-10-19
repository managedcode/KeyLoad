# Protected text results follow current persisted row ownership

REQ-FTS-ROW-OWNERSHIP-PUBLIC-001 supplements FullTextProjection REQ/AC-FTS-002/005 and ADR-078/081/125. Reuse actual TextClusterScenario protected fields and persisted RestrictRows reader with OwnerId=owner-a, Query/DocumentsRead/text-use grants. Establish a full independent visible text page. Replace that SAME document via actual SDK Put at ExpectedRevision1, preserving its indexed text while changing both persisted RowAccess owner and document owner field to owner-b. Both SDK and official MCP queries must successfully return complete empty pages for the still-matching text; old postings or private other seed documents cannot escape current row authorization. No FieldUse revocation occurs in this test.

AC-FTS-ROW-OWNERSHIP-PUBLIC-001 maps TextRowOwnershipPublicTests.CurrentRowOwnerConcealsExistingProtectedPostingsThenRepairAndOriginalReplaysPreserveLiteralPublicPages (one Test/no Arguments). Require initial independent reference/revision1, literal redacted JSON owner-a, all three exact redacted fields, rank1/61; after acknowledged hiding receipt revision2 require empty needle and Ukrainian pages through both public clients.

REQ-FTS-ROW-OWNERSHIP-PUBLIC-002: actual MCP Put at ExpectedRevision2 restores owner-a with identical indexed text and literal revision3. Retain actual original full CommandRequests and CommitReceipts. Replay the older hiding command through BOTH clients and require full current revision3 pages BEFORE replaying the later repair receipt; the latter must not conceal an incorrect current-owner regression. Replay later repair and require the same complete literal pages again. Acknowledged mutation receipt IDs, original partition and literal putDocument revisions2/3 remain exact.

AC-FTS-ROW-OWNERSHIP-PUBLIC-002 maps the same case: whole original receipts with ProcessDurable, independent MutationReceipt arrays, successful hidden-command retry while still hidden, visible repair, then older and newer full receipt replay bracketed by literal visible pages. Public page/receipt parity does not assert a raw canonical no-effects cut or ownership of read-generation metadata.

REQ-FTS-ROW-OWNERSHIP-PUBLIC-003 / AC-FTS-ROW-OWNERSHIP-PUBLIC-003: dedicated original keyed ClusterFixture, unchanged actual RequestCqrsClusterCallers.RunOwnedAsync owners and original test deadline retain body/MCP/HTTP/deadline failures with ServerFailureObserver. No new fixture/transport/config, projection deletion, mocked provider/context or caller-supplied role. No cold, cancellation or process-kill qualification is inferred.

```mermaid
flowchart LR
 Seed[Persisted restricted reader / protected indexed hit] --> Hide[SDK same-text Put / owner-b / revision2]
 Hide --> Empty[Both clients literal empty pages]
 Empty --> ReplayHidden[Full original hiding receipt retry]
 ReplayHidden --> Repair[MCP same-text Put / owner-a / revision3]
 Repair --> Visible[Full independent public pages]
 Visible --> OldReplay[Old hiding receipt replay]
 OldReplay --> StillVisible[Revision3 pages BEFORE newer replay]
 StillVisible --> NewReplay[Full repair receipt replay / healthy pages]
```

Canonical map: Integration Search Cases/TextRowOwnershipPublicTests, Helpers/TextRowOwnershipPublicTrial, Assertions/TextRowOwnershipPublicAssertions. TextClusterTests covers static restricted rows and warm changed-text/delete, TextAsyncClusterTests covers FieldUse revocation, FilteredSearchClusterTests.AcFilter002HiddenDeletedStaleAndCurrentWritesUseLiveEligibility preserves owner-a while changing text/deleting/vector refresh. None of these reviewed bodies changes Access ownership away and back with unchanged indexed text and full old/new receipts. The protected public cold case and the kernel full-text proof are separate.
