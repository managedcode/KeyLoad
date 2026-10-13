# Scoped time-series sample identity and atomic conflict public flow

Native AppendSamples defines the identity key by partition/set/series/EventId; the fingerprint includes sample content and canonical tags. Exact same identity/content under a new command is deduplicated without advancing the series sequence; different content conflicts. This public acceptance flow checks those identity decisions through both clients and actual RF3 cold stages.

| Requirement | Acceptance | Automated mapping |
|---|---|---|
| REQ-SERIES-ID-SCOPE-PUBLIC-001 retain independent same-ID records in sibling series | AC-SERIES-ID-SCOPE-PUBLIC-001 one genuine two-operation atomic command appends the same EventId at the same UTC timestamp to series A/value3 and B/value5. Both have independently literal sequence1 and full raw/latest/retention/aggregate results under a persisted SeriesRead-only caller; complete original receipt contains ordered appendSamples/A/1 and appendSamples/B/1 mutations. | ScopedSampleIdentityPublicTests and Seed/Expected/CallerReads/Checks |
| REQ-SERIES-ID-SCOPE-PUBLIC-002 deduplicate content identity while retaining distinct command receipts | AC-SERIES-ID-SCOPE-PUBLIC-002 a new original command with the exact two samples/tags returns a full own acknowledged receipt with series sequences still1; both raw series remain one row. Replay seed and echo separately via SDK and official MCP equals each captured entire original receipt; each replay/read is followed by canonical current public controls. | Seed/Observation/Checks |
| REQ-SERIES-ID-SCOPE-PUBLIC-003 reject content and tags conflicts without partial sibling effects | AC-SERIES-ID-SCOPE-PUBLIC-003 a mixed atomic command first stages a new sibling-B sample then reuses A's ID with value13, and another command reuses A's exact sample with tags changed17→18. Each SDK/official MCP invocation receives the complete native Conflict Problem/no success and fixed safe MCP detail. Immediately each refusal is followed by exact A/B raw/latest/retention/outbox plus healthy literal aggregates; staged sibling sample is absent. Public controls prove caller-visible preservation, not a physical journal/no-append claim. | Checks/Observation/Expected |
| REQ-SERIES-ID-SCOPE-PUBLIC-004 retain identity decisions through actual cold RF3 | AC-SERIES-ID-SCOPE-PUBLIC-004 join original callers, restart all3 same-volume Aspire-owned members, open fresh same credentials and rerun full matrix/refusals/original receipts. Repeat a second joined all3 cold and preserve every independent result; no provider/session/deadline/topology substitute. | Trial with original CredentialAuthorityCold and RequestCqrsRf3Callers |

Existing AcSeries009 covers warm same-series exact duplicate aggregation; SeriesUtcTiePublicTrial covers same-series identity replay after retention and time/sequence ties; ResourceTagPolicy flow covers projected policy changes. They do not cover sibling-series same-ID/different-content identity, mixed atomic conflict and tags fingerprint together with full public state and two cold cycles. Those original cases remain.

Architecture unchanged: TimeSeries ADR052/073/117/125, node-local ZoneTree and Orleans connection execution. Backend/contracts/SDK/server/UI N/A; no new surface/library/scalar mode or performance claim. Canonical new slice roles are IntegrationTests/Features/TimeSeries/Cases,Models,Helpers,Assertions and docs/Features/TimeSeries/ScopedSampleIdentityPublic.md. Use existing deadline and original cleanup. The code for this flow is authored; compilation, formatting, native test census, SDK/official MCP RF3 execution, exact-source Linux, fault, endurance and performance qualification remain pending.

```mermaid
flowchart LR
  Seed[Same ID independent sibling series] --> Echo[New command exact content deduplicates]
  Echo --> Conflict[Mixed value or tags conflict]
  Conflict --> Current[Each route full unchanged public controls]
  Current --> Cold[Two joined all3 cold cycles]
  Cold --> Replay[Full original receipts and identity preserved]
```

Complete persisted-reader raw/latest/retention observations run once per stage alongside independent administrator observations. After each refused or replayed commit, unchanged administrator raw/latest/retention/outbox and reader finite aggregates remain mandatory; these helpers do not call themselves. The raw interval includes the staged sibling timestamp so raw results independently expose any partial commit. MCP Conflict detail must equal the closed native safe literal, not the private sample-content detail. All roles retain the original deadline, with actual elapsed runtime acceptance OPEN.
