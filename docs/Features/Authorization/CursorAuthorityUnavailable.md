# KL-066: current cursor authority unavailable

REQ-AUTH-007 / AC-AUTH-007 and ADR-022 require current authority before payload disclosure at every request/page boundary. A correctly signed, unexpired cursor is insufficient when the actual persisted RF3 authority cannot reach its majority. No sensitive page may escape through SDK, official MCP, SQL wrapper or direct AST.

AC-AUTH-CURSOR-AUTHORITY-001 extends the SAME original `AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3` operation. After its real grant reduction, revocation and higher-epoch persisted repair, obtain six current independently verified first/second-page cursors. Keep the genuine existing Node2 SDK and official MCP callers alive. Kill both other original Aspire voters using fixture-owned faults; every current cursor must receive the exact bounded OwnershipLost/503 authority refusal with no result or credential/payload canary. No local token validation substitutes for current persisted policy. The original obsolete-policy cursor refusal is retained after authority restoration.

AC-AUTH-CURSOR-AUTHORITY-002 starts both owned stopped voters before joining readiness, under the existing independent bounded cleanup deadline, retaining all original fault/caller/cleanup errors. The SAME live caller owners then refuse original stale-policy cursors, return complete fresh literal healthy pages on all six routes and replay the complete original acknowledged seed receipt without another effect. Complete privileged literal row/resource state and every original OutboxStatus field remain unchanged; the physical read cut is monotone, not assumed equal across election entries. The original two joined all-three same-volume cold waves remain mandatory after this continuation.

```mermaid
flowchart LR
 A[Persisted repair ACK] --> B[Six current cursor pages]
 B --> C[Kill two original voters]
 C --> D[All pages refuse without payload]
 D --> E[Begin both voters then join readiness]
 E --> F[Same clients stale refusal and healthy pages]
 F --> G[Original receipt replay and two cold waves]
```

Backend production, public contracts, frontend and persisted formats are N/A: this is an additional missing real-operation fault criterion using the existing selected implementation, not a confirmed production defect. No new library, provider, identity authority, role, migration, transport or deadline. Native source/compiler/current-image runtime and exact-source Linux qualification remain separate OPEN gates. A prepared packet or a passing supporting test does not qualify the whole KL-066 scope.
