# Current persisted lease metadata authority

REQ-MSG-001 / AC-MSG-001 and ADR-026 require a current signed delivery lease to correspond to a real canonical claim before ACK, NACK, renew or cached-claim recovery. AddLeaseReadyInput persists Attempts at least one, StateVersion at least two and ReadySequence at least one. These exact minimum values follow from actual first enqueue and first claim; a lower value is an impossible retained claim row, not an earlier legitimate lease generation.

The current Lease check validates signed incarnation, lane, principal, state, owner, lease version, generation and expiry first. QueueLeaseAuthority then checks the retained IDs, the lease-index presence and the numeric metadata, so a matching current token cannot authorize impossible numeric metadata, expose a cached claim or rewrite that row through settlement: such a current row is rejected with Corruption and the static safe detail `The retained queue lease metadata is invalid.`. It never reconstructs the metadata or repairs storage on behalf of a caller. Existing signed-token scope, stale generation/ownership and expired-lease errors retain their earlier exact precedence. Missing deadline/index and foreign ID refusals remain unchanged.

| Criterion | Actual operation mapping | Required independent evidence |
|---|---|---|
| Current lease has the canonical numeric authority of an actual claim | Same `QueueLeaseAuthorityWholeFlowTests.CurrentLeaseRequiresExactRetainedAuthorityAcrossColdRepairOriginalAndHealthySettlement` | Add Ack/Nack/Renew × negative/zero Attempts, negative/zero/unclaimed StateVersion and negative/zero ReadySequence. Real claim/token are retained while only the actual metadata row is damaged. No fabricated token/provider is used. |
| Corruption never becomes a successful claim replay or partial settlement | Same existing QueueLeaseAuthorityRefusals owner | Original settlement and current cached-claim Apply throw exact Corruption; ResolveOutcome exposes the same safe no-payload failure. Full actual damaged store bytes and position remain unchanged after each refusal; no new settlement outcome, document or inbox effect exists. Repeat then join/reopen the same store and repeat. |
| Repair does not manufacture authority or discard original outcomes | Same original QueueLeaseAuthorityTrial/Healthy/Postimage owners | Restore exactly the retained original row, recover complete original claim, execute the original settlement and replay complete receipts, verify canonical indexes/counters and stale-consumed-claim behavior, complete healthy work, then reopen the same root and require exact original receipts and full stored image. |

The seven numeric fault variants cover the three settlement actions (Ack, Nack, Renew); the null-deadline and independent Processing cases remain. No public contract, serializer ID, storage format, retry/scheduler setting, timeout, clock, role or dependency is changed.

## Finite claim version space

REQ/AC-MSG-001 also preserves the normal monotonic claim invariant at its representational ceiling. Ready authority accepts positive max values, and AddLeaseReadyInput checks the Attempts, StateVersion and LeaseVersion increments for overflow: an exact int.MaxValue Attempts or long.MaxValue StateVersion/LeaseVersion refuses ResourceExhausted with the fixed safe detail `The queue claim counters are exhausted.` before staging any claim transition. There is no blanket exception catch, reset, saturation, reconstruction, alternate format or weakened fence.

`QueueClaimVersionExhaustionTests.ExhaustedClaimRetainsOneBoundedFailureAndNoLeaseThenColdExactRowRepairRequiresNewIdentityAndHealthyWork` executes three actual boundary variants. Seed an independently literal Ready message and producer receipt, preserve body/counters/indexes, change only the actual metadata field to its positive ceiling, then issue a new authorized stable claim. Its first failure legitimately persists exactly one failure outcome/clock commit; that metadata publication is distinguished from unchanged queue-domain bytes. Full literal Ready body/metadata/counters/ready reference and empty lease-index state remain exact. Complete failed result/retained outcome and full whole-store bytes and position are invariant on original Apply/Resolve replay and cold reopen.

Restore only the exact original metadata bytes at the fixture repair step. The original stable ID still returns its immutable failed outcome; it never becomes a success. A genuinely NEW Receive identity returns the complete literal current lease, then protected original ACK and fresh healthy enqueue/claim/ACK continue. The existing true same-root cold continuation owns actual producer/ACK receipt replays and independently literal terminal rows/counters; reopening its joined store confirms the original failure remains retained with no new effect.

This finite-ceiling scope adds three authored arguments to the twenty-one minimum metadata arguments. The original twenty leased-authority cases remain unchanged. These regressions use actual metadata and original signed operations, never a fabricated transport result or a substituted clock/provider.

## Finite settlement state version

REQ/AC-MSG-001 also freezes the related ACK/NACK/renew ceiling under REQ/AC-MSG-001. A real admitted Ready row at StateVersion long.MaxValue minus one can legitimately claim into a current Leased row at long.MaxValue. Earlier unchecked settlement increments could wrap that valid state. After the existing unsupported-action Validation check and original signed scope/stale/expiry Lease checks, but before Counters, lease-index deletion or any transition staging, that exact maximum now returns ResourceExhausted with `The queue delivery state version is exhausted.`. No earlier scope, stale-token, expiry or unsupported-action code is replaced.

`QueueDeliveryVersionExhaustionTests.ValidMaximumLeaseRefusesSettlementWithoutWrappingThenColdFixtureRepairPreservesFailedIdentityAndFreshHealthyWork` executes actual Ack/Nack/Renew variants. It seeds literal Ready input, changes only the fixture-owned numeric StateVersion to maximum minus one, and uses the real canonical claim to persist the maximum valid lease and signed token. First settlement failure persists one authorized failure/clock outcome while complete literal leased body/metadata/counters/lease/ready references and full queue-domain bytes remain unchanged. Original failed command Apply/Resolve and cold keep immutable failed result/outcome and whole-store bytes/position.

The fixture repairs only that chosen numeric field to the independently known normal first-claim StateVersion two, preserving every original actual lease field, body, index, token and signed identity. This is an explicit test-owned row repair; product execution never repairs or reconstructs corrupt/exhausted state. The original failed command remains failed. A NEW command with the same genuine token settles successfully. Renew retains its exact current lease; NACK retains scheduled input and counters before a later genuinely recorded due-time claim; ACK removes body and releases usage. Each action then completes original and fresh healthy work. NACK's promoted input uses new ReadySequence two and healthy work sequence three; ACK/renew healthy work sequence two. Same-root cold retains full original and healthy receipts, failure outcome and independently literal terminal state/counters; consumed old claim remains StaleLease while its original stored receipt stays exact.

All representational ceiling fixes form one coherent set.

## Expired Ready version space

REQ/AC-MSG-001 and actual Receive require monotonic expiry as well as claim. CaptureReadyItem validates current Ready authority and chooses expired input before AddLeaseReadyInput; the nonexpired claim guard cannot protect that branch. The expired Ready path rejects StateVersion long.MaxValue with ResourceExhausted and the existing static queue-claim detail before order/transition/body/counter staging. Attempts and LeaseVersion are not incremented on expiry and their positive ceilings remain permitted. No blanket claim guard is borrowed for this branch.

`QueueExpiredReadyVersionExhaustionTests.ExpiredReadyRequiresOnlyStateVersionSpaceAndRetainsOriginalOutcomeAcrossColdRepairAndHealthyWork` maps three genuine variants: exhausted StateVersion and successful nonincremented Attempts/LeaseVersion ceiling controls. Recorded enqueue time and ExpiresAt precede the recorded original Receive time; no clock provider is replaced, worker timer used or sleep/race assertion invented. Max-state failure persists its legitimate single failure/clock outcome while complete literal expired-due Ready body/metadata/counters/ready reference and full queue-domain bytes remain unchanged. Exact failure Apply/Resolve, retained outcome and full bytes/ position survive joined cold. Restore the exact original fixture metadata row; that old stable ID stays failed. A NEW Receive is empty and persists literal Expired version2 with original expiry, no body, zero usage and complete empty ready/lease/scheduled references. Positive max Attempts/LeaseVersion controls expire directly without rewriting those historical fields.

Before each actual producer, empty Receive, healthy claim and ACK, the fixture reads the actual partition witness and independently calculates the next commit position. Complete literal ProcessDurable producer/ACK receipts and ReceiveResult pages must match that full expected CommitToken, including the store incarnation; no returned token supplies its own expected authority. The healthy delivery additionally verifies its original server-signed claims against literal lane/message/principal/version/generation.

Fresh healthy enqueue/claim/ACK uses independent full payload, headers, metadata, counters and references. Original failed/empty outcomes and healthy producer/ACK receipts replay exactly after same-root cold; consumed healthy claim refuses StaleLease while its original stored result is unchanged.

```mermaid
flowchart LR
    A[Current signed lease] --> B[Validate retained numeric and index authority]
    B -->|invalid| C[Corruption, store unchanged]
    B -->|valid| D[Settle ACK NACK or renew]
    C --> E[Exact row repair]
    E --> D
```
