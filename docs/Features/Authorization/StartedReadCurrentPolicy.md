# Started read stream and current persisted policy

Requirements: REQ-AUTH-007 / AC-AUTH-007, [Authorization](../Authorization.md), [OrleansExecution](../OrleansExecution.md), [ADR-022](../../ADR/ADR-022-policy-epoch-revocation.md) and [ADR-125](../../ADR/ADR-125-connection-owned-execution.md).

REQ-STARTED-READ-001 / AC-STARTED-READ-001: an original database-signed ConnectionGrain read which has already produced and delivered its actual Started frame must still load current persisted authority after its existing operation barrier. Retaining the original verified subject/context/connection is not permission to retain the old principal policy. A real root-authorized revocation or expiry policy-change ACK while that original producer is held must prevent its later document payload. The same actual activation must independently serve an authorized sibling subject while the original remains held.

REQ-STARTED-READ-002 / AC-STARTED-READ-002: selected cancellation of that actual original stream must join its own enumerator and actual server ProducerDisposed observation without cancelling the sibling or a following operation. The read may report its original OperationCanceledException or the exact typed Cancelled terminal; UnknownWriteOutcome and success are not read-cancellation substitutes. No terminal/progress result may disclose document data after cancellation. Every actual initiating error and independent enumerator/context cleanup failure is retained through the existing ServerFailureObserver.

REQ-STARTED-READ-003 / AC-STARTED-READ-003: root-owned policy repair must restore fresh authorized reads on the SAME activation without reopening a cached permission path. The original separate writer's complete receipt, original stored outcome bytes, and independent literal current document remain conserved. Successful signed close joins original calls/observations and management must observe connection activation absence.

## Original whole-operation cases

StartedReadPolicyTests.OriginalStartedReadRequiresCurrentPersistedAuthorityAndSelectedCancellationLeavesSameConnectionHealthy has three Args: revoked, expired, cancelled. Each reuses the actual existing ConnectionScenario, RequestCqrsClusterFixture, Orleans TestCluster, generated serializer, actual ConnectionGrain, ManagedCode.Communication.CQRS IAsyncEnumerable, CqrsStreamLifetime, server-owned GrainRequestIdentityScope and real ZoneTree/replica-log/materializer admission. No fake stream/grain/clock/provider or product dispatcher is introduced. The existing phase observer pauses the original operation only after writer.StartedAsync; the new actual consumer independently observes that original Started frame before any policy action. Its original GetAsyncEnumerator/MoveNextAsync/DisposeAsync are awaited, not replaced by a fabricated result sequence.

The separate persisted writer first acknowledges the literal revision1 document. Its complete receipt is independently bound to the actual original retained replica entry index, physical incarnation/atomic partition/epoch, literal mutation and ProcessDurable profile. Hidden CompositionReferences are checked only as empty Booleans. Full original stored outcome bytes are retained verbatim. A separate reader's original signed read starts on the same server-owned connection, then the observer holds it. A real root ConfigurePrincipal ACK sets reader Revoked=true/epoch2 or ExpiresAt=UnixEpoch/epoch2. The expiry variant proves a newly acknowledged persisted past expiry against the unchanged TimeProvider.System; it is not evidence of crossing a timed wall-clock boundary, envelope expiry, API-key expiry or a fake clock advance.

An independent writer read completes with the full literal DocumentResult while the original remains incomplete. After releasing the held producer, the original frames must be exactly Started then Failed/Unauthenticated with the complete independently expected credential-unavailable Problem and no reply value. The cancellation variant instead cancels only its original linked token and preserves the original cancellation object or exact Failed/Cancelled Problem. No Progress or Completed frame is allowed. After actual producer disposal, complete canonical key/value bytes, physical store position and original writer outcome bytes remain exact to the post-ACK snapshot.

The original and sibling request IDs/subjects, actual GrainId/ActivationId and producer-disposed observations prove one reused activation and isolated identities. Actual epoch3 repair restores the same reader's original grants and removes expiry/revocation; a fresh real same-activation read returns the complete original document. The unchanged writer command replays the actual original reply payload and stored outcome bytes without a new domain effect. These are original observed reply bytes, not a reencoding of a public receipt as persistence proof. The original signed connection close and management observation join all owners before existing fixture shutdown.

```mermaid
flowchart LR
 Seed[Actual acknowledged document] --> Started[Original Started frame observed]
 Started --> Hold[Existing real request phase holds producer]
 Hold --> Policy[Persisted revoke or expiry ACK]
 Hold --> Cancel[Selected original cancellation]
 Policy --> Sibling[Other subject completes on same activation]
 Cancel --> Sibling
 Sibling --> Denied[Original has no document or progress payload]
 Denied --> Join[Original enumerator and actual producer disposal joined]
 Join --> Repair[Persisted repair and fresh literal read]
 Repair --> Replay[Original writer receipt and stored bytes retained]
 Replay --> Close[Signed close and management absence]
```

## Scope, ownership and gates

This is a distinct begun-read checkpoint regression. Existing fresh-after-revocation command tests, held-write authority faults, ordinary SQL/AST cursor repair/two-cold, scalar live-current-policy continuation and EventFeedTail cancellation/current-refusal flows remain unchanged. It does not duplicate them or introduce another SQL/live/tail scenario. Product, existing fixture/provider/serialization identities, routing limits, deadlines and clocks are untouched. Frontend/public HTTP/MCP/new infrastructure N/A for this foundation stage.

The fixture has one actual Orleans test silo and real node-local ZoneTree/CLUSTER-configured journal/materializer admission, with no real three-node replicated cluster. Its ProcessDurable receipt does not prove Docker/Aspire cluster, public SDK/official MCP, a long multi-page tail, process restart, two cold owners, live expiry timing or complete acceptance. The complete raw image oracle is bounded and source-known. Existing fixture owns the actual cluster/request-work owner/store shutdown; the new consumer joins its original task and producer before that shutdown, retaining all actual cleanup errors.

Prepared parse/numeric/nesting preflight, source/API review and retained guards are not semantic compilation or execution. No SDK/build/test/Git/SourceY command was run by this lane. No task-status or global-plan edit is proposed.

## Started frame representation

The original output records only Boolean equality failure, not the actual serialized frame; it is retained and does not prove a policy enforcement defect.

The independently expected frame must describe the actual Orleans boundary. ManagedCode.Communication and its Orleans package are pinned to 10.3.7, whose NuGet repository identity is commit 3dfe26393eb472e476572db8fb000c3ea35699bf. The original ConnectionGrain writes Started with the independently generated request ID and first sequence 1. The pinned [chunk converter](https://github.com/managedcode/Communication/blob/3dfe26393eb472e476572db8fb000c3ea35699bf/ManagedCode.Communication.Orleans/Converters/CqrsStreamChunkSurrogateConverter.cs) stores the resolved EventType into its surrogate and reconstructs that explicit value. Therefore the independent complete expected Started frame explicitly sets the literal cqrs-started; a factory-only chunk with raw EventType unset omits that member from JSON and is the wrong representation oracle for this post-Orleans observation. No observed field is copied into the expectation.

Full canonical JSON equality remains exact, including request ID, progress Result, sequence and event metadata; Started-only diagnostic string equality reports the complete expected/actual Started frame instead of an opaque Boolean. That frame contains only request ID, progress and protocol metadata; generic document/receipt/problem equality retains its original Boolean oracle without logging caller payload. Existing failure, selected cancellation, independent writer/reader policy, original raw outcome, healthy same-activation read, replay and joined close assertions remain unchanged. This fixes a source-confirmed oracle mismatch. The next original runtime cohort must determine whether it explains the entire actual failure; no successful rerun, new failure injection or additional two-cold/cluster proof is claimed here.
