# Current persisted lease metadata authority — KL-087

REQ-MSG-001 / AC-MSG-001 and ADR-026 require a current signed delivery lease to
correspond to a real canonical claim before ACK, NACK, renew or cached-claim
recovery. AddLeaseReadyInput persists Attempts at least one, StateVersion at least
two and ReadySequence at least one. These exact minimum values follow from actual
first enqueue and first claim; a lower value is an impossible retained claim row,
not an earlier legitimate lease generation.

Semantic source review confirmed that current Lease checks signed incarnation,
lane, principal, state, owner, lease version, generation and expiry first. The
subsequent QueueLeaseAuthority previously checked retained IDs and lease-index
presence only; a matching current token could therefore authorize impossible
numeric metadata and expose a cached claim or rewrite that row through settlement.
This correction rejects such a current row with Corruption and the static safe
detail `The retained queue lease metadata is invalid.`. It never reconstructs the
metadata or repairs storage on behalf of a caller. Existing signed-token scope,
stale generation/ownership and expired-lease errors retain their earlier exact
precedence. Missing deadline/index and foreign ID refusals remain unchanged.

| Criterion | Actual native operation mapping | Required independent evidence |
|---|---|---|
| Current lease has the canonical numeric authority of an actual claim | Same `QueueLeaseAuthorityWholeFlowTests.CurrentLeaseRequiresExactRetainedAuthorityAcrossColdRepairOriginalAndHealthySettlement` | Add Ack/Nack/Renew × negative/zero Attempts, negative/zero/unclaimed StateVersion and negative/zero ReadySequence. Real claim/token are retained while only the actual native metadata row is damaged. No fabricated token/provider is used. |
| Corruption never becomes a successful claim replay or partial settlement | Same existing QueueLeaseAuthorityRefusals owner | Original settlement and current cached-claim Apply throw exact Corruption; ResolveOutcome exposes the same safe no-payload failure. Full actual damaged store bytes and position remain unchanged after each refusal; no new settlement outcome, document or inbox effect exists. Repeat then join/reopen the same store and repeat. |
| Repair does not manufacture authority or discard original outcomes | Same original QueueLeaseAuthorityTrial/Healthy/Postimage owners | Restore exactly the retained original row, recover complete original claim, execute the original settlement and replay complete receipts, verify canonical indexes/counters and stale-consumed-claim behavior, complete healthy work, then reopen the same root and require exact original receipts and full stored image. |

The seven numeric fault variants add twenty-one authored arguments for the three
KL-087 settlement actions. All original twenty argument mappings, including null
deadline and independent Processing cases, remain untouched; the task does not
expand the separate inbox/processing feature. Native discovered inventory/UIDs,
compiler/analyzers/formatter, focused current-SIMD wholeflows, process recovery,
Aspire RF3 SDK/official MCP and delivered-source Linux are separate mandatory
gates. Source authoring is not runtime proof or KL-087 acceptance.

Ownership: Core Features/Messaging/Validation/QueueLeaseAuthority.cs and the
existing Unit Messaging Cases/Contracts/Helpers/Assertions owners. No shared
Messaging.cs, public contract, native serializer ID, storage format, migration,
retry/scheduler setting, timeout, clock, role or dependency is changed. Rollback
removes this guard and its additive fault cases coherently; it does not silently
repair a corrupt database or turn old outcomes into current lease authority.

## Finite claim version space

The same REQ/AC-MSG-001 batch also preserves the normal monotonic claim invariant
at its representational ceiling. Existing Ready authority accepts positive max
values, while AddLeaseReadyInput previously incremented Attempts and StateVersion
without overflow checking; LeaseVersion overflow escaped through checked native
arithmetic. Exact int.MaxValue Attempts or long.MaxValue StateVersion/LeaseVersion
now refuses ResourceExhausted with the fixed safe detail `The queue claim counters
are exhausted.` before staging any claim transition. There is no blanket exception
catch, reset, saturation, reconstruction, alternate format or weakened fence.

`QueueClaimVersionExhaustionTests.ExhaustedClaimRetainsOneBoundedFailureAndNoLeaseThenColdExactRowRepairRequiresNewIdentityAndHealthyWork`
executes three actual native boundary variants. Seed an independently literal Ready
message and producer receipt, preserve body/counters/indexes, change only the actual
metadata field to its positive ceiling, then issue a new authorized stable claim.
Its first failure legitimately persists exactly one failure outcome/clock commit;
that metadata publication is distinguished from unchanged queue-domain bytes. Full
literal Ready body/metadata/counters/ready reference and empty lease-index state
remain exact. Complete failed result/retained outcome and full whole-store bytes
and position are invariant on original Apply/Resolve replay and cold reopen.

Restore only the exact original native metadata bytes at the fixture repair step.
The original stable ID still returns its immutable failed outcome; it never becomes
a success. A genuinely NEW Receive identity returns the complete literal current
lease, then protected original ACK and fresh healthy enqueue/claim/ACK continue.
The existing true same-root cold continuation owns actual producer/ACK receipt
replays and independently literal terminal rows/counters; reopening its joined
store confirms the original failure remains retained with no new effect.

This finite-ceiling scope adds three authored arguments to the twenty-one minimum
metadata arguments. The original twenty leased-authority cases remain unchanged.
Native compiler/discovery/runtime/process/RF3/Linux proof remains OPEN. These
regressions use actual native metadata and original signed operations, never a
fabricated transport result or a substituted clock/provider.

## Finite settlement state version

The same batch freezes the related ACK/NACK/renew ceiling under REQ/AC-MSG-001.
A real admitted Ready row at StateVersion long.MaxValue minus one can legitimately
claim into a current Leased row at long.MaxValue. Earlier unchecked settlement
increments could wrap that valid state. After the existing unsupported-action
Validation check and original signed scope/stale/expiry Lease checks, but before
Counters, lease-index deletion or any transition staging, that exact maximum now
returns ResourceExhausted with `The queue delivery state version is exhausted.`.
No earlier scope, stale-token, expiry or unsupported-action code is replaced.

`QueueDeliveryVersionExhaustionTests.ValidMaximumLeaseRefusesSettlementWithoutWrappingThenColdFixtureRepairPreservesFailedIdentityAndFreshHealthyWork`
executes actual Ack/Nack/Renew variants. It seeds literal native Ready input,
changes only the fixture-owned numeric StateVersion to maximum minus one, and
uses the real canonical claim to persist the maximum valid lease and signed token.
First settlement failure persists one authorized failure/clock outcome while
complete literal leased body/metadata/counters/lease/ready references and full
queue-domain bytes remain unchanged. Original failed command Apply/Resolve and
cold keep immutable failed result/native outcome and whole-store bytes/position.

The fixture repairs only that chosen numeric field to the independently known
normal first-claim StateVersion two, preserving every original actual lease field,
body, index, token and signed identity. This is an explicit test-owned row repair;
product execution never repairs or reconstructs corrupt/exhausted state. The
original failed command remains failed. A NEW command with the same genuine token
settles successfully. Renew retains its exact current lease; NACK retains scheduled
input and counters before a later genuinely recorded due-time claim; ACK removes
body and releases usage. Each action then completes original and fresh healthy
work. NACK's promoted input uses new ReadySequence two and healthy work sequence
three; ACK/renew healthy work sequence two. Same-root cold retains full native
original and healthy receipts, failure outcome and independently literal terminal
state/counters; consumed old claim remains StaleLease while its original stored
receipt stays exact.

These three authored settlement arguments join the three claim ceiling arguments
and twenty-one minimum-metadata arguments without replacing any original case.
All representational ceiling fixes form one coherent batch before source join and
qualification. No claim follows from this source-only contract.
