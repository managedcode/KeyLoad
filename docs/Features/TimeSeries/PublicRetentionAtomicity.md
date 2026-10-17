# TimeSeries: public retention and atomic sample identity

Canonical slice: TimeSeries. This scenario extends REQ/AC-SERIES-004 and
REQ/AC-SERIES-013–016 under ADR-052, ADR-073, ADR-010 and ADR-125. It exercises
existing database behavior through the real .NET SDK and official MCP SDK in
the Aspire-owned RF3 fixture. Core, transport, storage format and limits do not
change.

| Requirement | Measurable acceptance and automated mapping |
|---|---|
| REQ-SERIES-PUBLIC-RETENTION-001: expiry preserves original sample identity | AC-SERIES-PUBLIC-RETENTION-001: commit literal samples known at T/value3/sequence1 and floor at T+1tick/value5/sequence2; expire exclusively before T+1tick with MaximumDeletes2. Both clients return the full literal visible row, latest, aggregate, dense windows and retention status Before=T+1tick/PurgedCount1/HasMore=false. Mapping: Preparation, Expected and Current. |
| REQ-SERIES-PUBLIC-RETENTION-002: fresh commands deduplicate a purged known identity | AC-SERIES-PUBLIC-RETENTION-002: separate new SDK-first and MCP-first command IDs append the original known ID with its original timestamp/value/tags. Each complete receipt has appendSamples revision2 and authentic RF3 acknowledgment; no expired row reappears and every current DTO remains exact. Mapping: Preparation, Trial and Replay. |
| REQ-SERIES-PUBLIC-RETENTION-003: changed identity content conflicts before retention classification | AC-SERIES-PUBLIC-RETENTION-003: separate fresh SDK-first/MCP-first commands change the purged known ID to value13 at its original below-floor timestamp. Each SDK returns no value and the complete Conflict Problem with detail “A sample ID was reused with different content.” Official MCP returns a dispatched Conflict error with its original safe generic detail. Original command retries retain the same result. Mapping: Refusals and Current. |
| REQ-SERIES-PUBLIC-RETENTION-004: a mixed batch rolls back all staged sample effects | AC-SERIES-PUBLIC-RETENTION-004: each distinct SDK-first/MCP-first command orders a valid fresh ID at T+2ticks/value7 before a genuinely new ID at T/value11. Both refuse HistoryUnavailable with the complete SDK detail “A sample cannot be appended before the series retention floor.” and the dispatched MCP safe generic error. Full current range includes the staged timestamp but omits that staged ID; latest, aggregate, every dense window, floor/count and public outbox remain unchanged after each call and retry. Mapping: Refusals and Current. A failed original outcome may be persisted; this is not a full-store no-effects assertion. |
| REQ-SERIES-PUBLIC-RETENTION-005: rejected staging does not consume sample identity or sequence | AC-SERIES-PUBLIC-RETENTION-005: after the first joined cold restart, a genuine MCP-first fresh command commits exactly the same formerly staged fresh ID/value/timestamp. Its complete appendSamples receipt reports revision3. The only visible rows are floor/sequence2/value5 and fresh/sequence3/value7, with aggregate Count2/Sum12/Min5/Max7/Mean6 and all literal dense windows. Mapping: Trial, Expected and Current. |
| REQ-SERIES-PUBLIC-RETENTION-006: original successful receipts replay without resurrection | AC-SERIES-PUBLIC-RETENTION-006: replay the complete original seed, expiry, both dedup receipts and eventual fresh receipt through each client after their relevant phase. Require complete original receipt equality and full unchanged current DTOs/outbox after every replay. Mapping: Replay and Current. |
| REQ-SERIES-PUBLIC-RETENTION-007: persisted authority and outcomes survive both joined cold boundaries | AC-SERIES-PUBLIC-RETENTION-007: dispose and join all original SDK/MCP caller owners before each actual all-three-node Aspire restart. Reconnect using the same persisted SeriesRead principal and secret, assert the full literal current results, re-exercise original refused commands, and replay every successful receipt. The first cold retains sequence2/floor state; the second retains the genuine fresh sequence3 state. Mapping: Trial, Refusals and Replay. |
| REQ-SERIES-PUBLIC-RETENTION-008: resource and qualification contracts remain honest | AC-SERIES-PUBLIC-RETENTION-008: one genuine NoArgs TUnit case uses the unchanged shared two-minute deadline, original RF3 topology and owned caller cleanup. No fake provider, new clock/limit, scalar product mode, compatibility fallback or generated test UID. Compilation, native TUnit execution and exact-source Linux RF3 evidence are required independently before acceptance. Mapping: SeriesRetentionAtomicPublicTests and Trial. |

The public DTO oracles are independent literals: tags are {"visible":17}; T is
the existing fixed UTC scenario start; raw ranges include T through T+3ticks;
aggregate/window reads are half-open through T+3ticks. The three one-tick
windows include empty windows before the floor. Expiry deletes only canonical
sample rows; the existing identity fingerprint remains authoritative. The mixed
request stages its valid item first so the later refusal qualifies rollback.

Official MCP failure detail is exactly “The database operation could not be
completed.” Its envelope must report the expected error and actual dispatch.
SDK failures compare the complete Problem returned by the existing Errors API,
including no result value. Public current-data and outbox assertions are distinct
from the internal persisted failed-outcome journal.

```mermaid
flowchart LR
    Seed[Original two-sample RF3 receipt] --> Floor[Logged exclusive expiry]
    Floor --> Echo[Fresh SDK and MCP dedup commands]
    Echo --> Refusal[Changed ID and valid-then-expired mixed refusals]
    Refusal --> Cold1[Joined three-node cold restart]
    Cold1 --> Fresh[Genuine fresh sample gets sequence 3]
    Fresh --> Replay[Complete original receipts and current results]
    Replay --> Cold2[Second joined cold restart]
    Cold2 --> Verify[Same persisted reader and complete literal state]
```

Canonical implementation map:
- tests/KeyLoad.IntegrationTests/TimeSeries/Cases/SeriesRetention/SeriesRetentionAtomicPublicTests.cs: one real TUnit declaration.
- Models/SeriesRetentionAtomicPublicState.cs: bounded scenario state and original commands/receipts.
- Assertions/SeriesRetentionAtomicPublicExpected.cs: independent complete DTO literals.
- Helpers/SeriesRetentionAtomicPublicPreparation.cs: persisted identity, original placement and commands.
- Assertions/SeriesRetentionAtomicPublicCurrent.cs: full SDK/MCP read and public outbox comparisons.
- Assertions/SeriesRetentionAtomicPublicRefusals.cs: exact original SDK/MCP failures and healthy current controls.
- Helpers/SeriesRetentionAtomicPublicReplay.cs: complete successful receipt replay.
- Helpers/SeriesRetentionAtomicPublicTrial.cs: genuine operations, caller ownership and both cold boundaries.

Backend/public contracts/client implementation: N/A, existing native APIs are
unchanged. Frontend and benchmarks: N/A, this is correctness acceptance rather
than a UI or performance change. Qualification: implementation authored;
compiler, runtime and exact-source Linux RF3 results remain pending. Process
restart does not establish power-loss durability, production readiness or speed.
