# Exact named parameters through public RF3 SQL operations

REQ-TSQL-002/003/005/006 and AC-TSQL-002/003/005/006 in [ScriptDOM](ScriptDom.md), under [ADR-130](../../ADR/ADR-130-microsoft-scriptdom-tsql.md), [ADR-125](../../ADR/ADR-125-connection-owned-execution.md) and [ADR-117](../../ADR/ADR-117-native-tunit-ci-entry.md), retain the existing bounded typed database operations. This is public wire-key parity alongside the separate Unit dictionary-comparer repair. A Dictionary comparer does not survive SDK/MCP JSON serialization; this flow makes no public comparer-regression claim.

## Requirements and whole-operation map

| Requirement | Actual complete operation and independent oracle |
|---|---|
| REQ-NAMED-PUBLIC-001 / AC-NAMED-PUBLIC-001 | Existing SqlParserAdmissionRf3Setup creates a genuine fresh typed row through named id/title/amount INSERT. The original literal row, putDocument mutation/revision, command GUID, actual quorum receipt and original outbox assertions remain. |
| REQ-NAMED-PUBLIC-002 / AC-NAMED-PUBLIC-002 | TOP(@take) and EVENTS(@collection,@stream,@generation) run through SDK Query, official MCP Query, SDK SQL envelope and official MCP SQL envelope. Correct keys require independent complete four-document/two-event literal pages, exact metadata and empty cursor. Each uppercase-only key refuses full fixed Validation, then the same original clients execute the correctly keyed request. |
| REQ-NAMED-PUBLIC-003 / AC-NAMED-PUBLIC-003 | Uppercase-only AMOUNT refuses before mutation dispatch through SDK and official MCP. The same original named INSERT GUID then replays the first typed/public receipt; hidden composition references are checked only as IsEmpty, never disclosed or byte-reencoded as a wire oracle. |
| REQ-NAMED-PUBLIC-004 / AC-NAMED-PUBLIC-004 | Existing Current pure checks conserve every seeded public document, typed row, stream/head and original complete outbox; seed/event/INSERT receipts replay. The original persisted reader denial/exact repair, parser/AST/refusal/cancellation/unicode/numeric controls and two genuine owned cold transitions remain. |
| REQ-NAMED-PUBLIC-005 / AC-NAMED-PUBLIC-005 | Existing caller deadlines and RunOwnedAsync join original SDK/official MCP transports before ModelSqlRf3Cold. Keyed fixture retention is enabled before Initialize, retaining body or cleanup failures while original fixture shutdown still joins. |

```mermaid
flowchart LR
  Seed[Fresh named INSERT and original receipt] --> Keys[SDK and official MCP exact-key reads]
  Keys --> Refuse[Uppercase-only key Validation and complete no-effect image]
  Refuse --> Repair[Same clients exact-key healthy read and same-GUID replay]
  Repair --> Policy[Original persisted deny and exact repair]
  Policy --> Join[Join original clients and genuine RF3 cold restart]
  Join --> Keys
```

The existing NativeAnnotationsAndPrivateLexicalBoundsRefuseThenCanonicalSqlAstAndTypedInsertReplaySurviveTwoColdOwners declaration is retained, with the same real Aspire RF3 fixture, resources, discovered endpoints and server-owned connections. Existing global serialization is retained because the original whole operation kills/restarts the actual shared-key RF3 cohort; no new scheduling restriction is introduced. The same two cold waves each repeat these wire-key checks without writes beyond original GUID replay. Public local cuts are opaque and only bounded positive witnesses; no physical-owner or equal cross-node cut proof is claimed.

SourceN/numeric review is not execution. Integrated build/analyzers/format, exact native declaration discovery, real SDK/official MCP Docker RF3 outcomes, Linux/source-image/coverage/fault/endurance and full SQL/client protocol qualification remain OPEN. No product, parser, package, transport, provider, host, quota, timeout, public API, schema or persisted-format change is included.
