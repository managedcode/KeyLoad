# ClientApi

Shared authenticated .NET SDK/CLI transport. [ADR-035](../ADR/ADR-035-memory-performance.md)
and [memory/performance acceptance](../../memory-performance.acceptance.md) govern
the current transport repair. The typed operation contracts and server authority
remain unchanged.
ADR-041 governs the explicit read-only CLR migration and the strict-analysis
query factory move to KeyLoadQuery.From<T>. Generated AST/JSON, cursor, authority
and transport behavior remain exact; all compiled consumers recompile together.

| Requirement | Acceptance | Automated proof |
|---|---|---|
| REQ-CLIENT-001: consume success bodies without a duplicate full HTTP response buffer | AC-MP-009 | Real Kestrel chunked/delayed typed response; RF3 SDK public calls |
| REQ-CLIENT-002: bounded error parse, cancellation and disposal preserve caller outcomes | AC-MP-009 | Real server malformed/oversized errors, mid-body cancellation, subsequent request |
| REQ-CLIENT-003: operator profiles are bounded and valid generated profiles preserve semantics | AC-MP-010 | Real temporary profile files in TUnit, oversized rejection before full allocation |

Canonical ownership: transport/helpers in src/KeyLoad.Client/Features/ClientApi;
tests in tests/KeyLoad.UnitTests/Features/ClientApi or the matching integration
slice. Existing KeyLoadClient.cs is the cross-feature transport entry point;
business operation methods retain their typed canonical contracts. CLI profile
adaptation belongs to ClientApi; shared AppHost composition is lead-owned.
Frontend/database/schema changes: N/A, transport decoding only.

CLI composition now lives in `src/KeyLoad.Cli/Hosting/KeyLoadCliApplication.cs`;
status/profile/help behavior lives in its `Features/ClientApi/CliClientApi.cs` and
original-text resource. The preserving contract and review exception are
AC-CQ-010 in [quality-gates acceptance](../../quality-gates.acceptance.md), under
ADR-033. The actual CLI build is clean; its process qualification remains pending.

```mermaid
sequenceDiagram
    participant Caller
    participant SDK
    participant Server
    Caller->>SDK: Typed operation and cancellation
    SDK->>Server: Authenticated request
    Server-->>SDK: Headers then streamed JSON
    SDK-->>Caller: Typed result or bounded mapped failure
    SDK->>SDK: Dispose response after decoding
```

TASK-MP-008 owns only the assigned SDK transport plus new matching helper/test
files. Lead owns shared docs/config and reviews the join. Real ASP.NET/Kestrel
network verification avoids fake HttpMessageHandlers or stream doubles. TUnit on
Microsoft.Testing.Platform runs only in GitHub Actions; development builds are not
test qualification. Unknown committed-write outcomes retain existing semantics;
an aborted decode never proves that the server rolled back the operation.

## Accepted real-Kestrel cancellation coordination refinement

TASK-RUNTIME-Kestrel-W preserves REQ-CLIENT-002 / AC-MP-009/012 under ADR-035.
The macOS baseline timed out before cancellation at the initial 1 MiB write/flush
barrier. Write and flush a bounded initial partial-body chunk, then withhold the
remaining response until caller cancellation. Surface named handler/write/flush
stages and unexpected handler failures under the same five-second waits. Preserve
the pending incomplete response, Cancelled error, real request-aborted signal and
successful subsequent HTTP request. Worker owns only KeyLoadClientTransportTests.cs;
no client product, timeout, policy or response-mapping change. Lead reviews the
real lifecycle and qualifies all three OSes in the complete GitHub run.

## Accepted root-name framing repair

TASK-RUNTIME-MCP-FRAME-W implements REQ-CLIENT-006 and AC-MCP-003/005/007 under
ADR-039. Run37005805424 proves lone-surrogate root property names can throw during
ValueTextEquals before safe string decoding. Classify a root id from the same
validated decoded property name used by duplicate detection. Preserve encoded
name/id byte limits, safe fixed Validation, depth/count budgets, escaped id keys,
native scalar ids and valid surrogate pairs; add no broad exception handler.
The existing four name/value surrogate cases and escaped-id bounds are failing/
boundary regressions. Worker owns only McpFrameInspection.cs; lead owns docs,
review, enabled build and exact GitHub UnitTests/RF3 join. No wire/persistence or
package migration; rollback reverts this internal property-classification change.

## Accepted malformed-problem decoding repair

REQ-CLIENT-002 / AC-MP-009 owns TASK-RUNTIME-ERROR-DECODING-W under ADR-035.
Run37005805424 reproduces a malformed problem body escaping the bounded reader
and selecting the transport-read detail instead of the server-unavailable fallback.
Only JsonException from the bounded Problem deserialization becomes a missing
problem; cancellation, I/O and success-body mapping remain intact. The existing
real-Kestrel ErrorBodiesArePreservedWhenValidAndBoundedWhenNullMalformedOrOversized
test is the failing regression. It retains exact valid details, malformed/null/
65,537-byte fallbacks, UnknownWriteOutcome and the original command header.
Lead owns the private reader and shared contract join. Release build, format,
governance and full exact-SHA GitHub suites qualify the repair. No public API,
schema or package migration is needed; rollback reverts only this private catch.

## Повний caller contract

### Accepted unknown-length MCP framing repair

REQ-CLIENT-009 / AC-CLIENT-009, AC-MCP-001/004/005/007 and AC-MP-009/012
(TASK-RUNTIME-MCP-FRAME-W3) require the unmodified official2.2.0 discovery-first
SDK to succeed under the existing default node/scope/memory limits. Its native
JsonContent has unknown HTTP length. At fa80c701, a small such body allocates the
8MiB permitted maximum, and admission carries an ingress projection already
240,947,200 bytes into the default134,217,728-byte control pool. The prior RF3
receipt lacks the exact admission exception, so this is a source-derived cause,
not a captured memory failure. Keep first discovery proof in renewed RF3 CI.

Separate permitted wire bytes from actual retained buffer capacity. For unknown
length, begin with at most the existing16KiB scratch-sized buffer and grow only
as actual bytes arrive, explicitly clamping growth to the same inclusive limit.
Known-length declarations, one excess byte, truncated bodies, UTF8/JSON/shape
errors, cancellation, replay bytes and clearing the entire retained buffer remain
exact. Ingress retains its conservative pre-read maximum reservation. After
framing, the canonical admission handoff uses the actual owned buffer capacity
and inspected shape; it cannot undercharge any retained raw owner. Do not change
projection equations, pool defaults, quotas, accepted protocol or client headers.

Server/ClientApi McpFrameBody and McpRequestState plus UnitTests/ClientApi real
frame/state regressions are the worker scope. Existing RF3 McpDiscoveryTests must
connect with default negotiation, list all tools and execute canonical calls;
scope/credential invalidation and malformed/oversized negatives remain required.
Tests first prove a small unknown-length frame's retained capacity, exact replay,
bounded growth/overrun and admission under the unchanged default control pool,
then disposal releases execution/memory ownership. Root owns review/build/format
and exact multi-OS unit plus RF3 official SDK join. Frontend, SDK API, persisted
format and dependency migration are N/A; ADR039 owns source-only rollback.

### Accepted MCP rejection diagnostics

REQ-CLIENT-008 / AC-CLIENT-008 (TASK-RUNTIME-MCP-DIAGNOSTICS-W) refines
REQ-CLIENT-006 and AC-MCP-003/005/007 after run37015193756. Every existing transport
guard rejection carries a closed private stage: revision count/value, session or
last-event presence, method/name header shape/encoding, body method shape or
mismatch, missing target, parameter/meta revision mismatch or target mismatch.
The public Validation detail and every accept/reject predicate remain exact.
Only failure paths attach enum metadata; successful calls gain no diagnostic
allocation, payload copying or changed business routing.

Logging uses only defined stage enums and Discovery/Initialize/ToolsCall/ToolsList/
Other method categories. Unknown metadata or arbitrary method strings become
closed defaults; no raw headers, arguments, URI/name, identity, bearer credential,
exception object/text or serialized body can enter the logger. Tests exercise the
actual guard and Microsoft logging EventSource provider with private canaries,
invalid enum metadata, malformed headers/body and unchanged safe errors. All
existing guard tests remain. The lead joins logging in McpHttpPipeline and owns
shared EventSource filter coordination, preserving identical native SDK handling.
Source privacy/predicate/lifetime review, enabled build and exact-SHA GitHub unit
and RF3 receipts are required. The environmental first-discovery failure requires
actual CI receipts rather than a synthetic caller or fabricated success.

Server/UnitTests Features/ClientApi is the canonical map; frontend, data migration
and SDK/API shape are N/A because this is internal failure evidence only. ADR039
governs ordered implementation and source-only rollback. No protocol downgrade,
dependency patch, broad exception fallback, trusted caller role or timeout change.

Актори: application developer, operator CLI, durable worker і required MCP/agent caller. Source-present: typed [.NET SDK](../../src/KeyLoad.Client/KeyLoadClient.cs), [query builder](../../src/KeyLoad.Client/KeyLoadQuery.cs), [CLI](../../src/KeyLoad.Cli/Program.cs) та [HTTP routes](../../src/KeyLoad.Server/ApiEndpoints.cs). Public capability/operation semantics визначає owning Feature; SDK не є другим engine.

| Вимога | Measurable acceptance / flows | Test mapping |
|---|---|---|
| REQ-CLIENT-004: supported typed operations і capabilities дзеркалять canonical server contracts | AC-CLIENT-004: existing document/event/queue/group/graph/series/query/search/feed/admin calls повертають typed values/outcomes; unsupported capability/version дає явну помилку; equivalent query adapters дають однаковий result/authority | Existing `SqlJsonAndCSharpUseTheSameAuthorizedHttpQueryContract` у [ClusterTests](../../tests/KeyLoad.IntegrationTests/ClusterTests.cs), [QueryAdapterTests](../../tests/KeyLoad.UnitTests/QueryAdapterTests.cs); operation-manifest parity expansion PLANNED |
| REQ-CLIENT-005: retry/error/cancellation зберігають stable command identity та unknown outcome | AC-CLIENT-005: transient disconnect після committed write не спричиняє другу logical operation; same ID/content replay стабільний, different payload conflict; typed errors, cancellation, bounded decode/disposal зберігають caller semantics без fabricated success | Existing `ReplicatedAtomicBatchSurvivesLeaderProcessKillAndMinorityRejectsWrites` у ClusterTests; transport edge/error cases з AC-MP-009 PLANNED/GitHub pending |
| REQ-CLIENT-006: official MCP SDK adapter виконує ті самі authorized operations | AC-CLIENT-006 and AC-MCP-001–008: real official MCP C# SDK caller на Docker RF3 має operation/result/error/cancellation parity з .NET SDK; invalid/forged/revoked grants та unsupported calls fail before effects; search/storage coverage включено лише після owning capability implementation | Source IntegrationTests `Features/ClientApi/` MCP parity/adversarial suite; [ADR-039](../ADR/ADR-039-official-mcp-agent-api.md) accepted stateless `/mcp`, version-one 50-operation source catalog including ten ADR-038 BlobStorage operations, three read-only ADR-051 AdminDashboard operations and bounded admission contract; runtime pending |
| REQ-CLIENT-007: simple agent/worker API має bounded capability та processing contract | AC-CLIENT-007: PLANNED versioned agent calls користуються тим самим persisted principal, typed operations, quotas і outcome semantics; queue worker stale lease/duplicate handler, empty input та cancellation не дають unauthorized/duplicate effects | Existing processing semantics у [MessagingTests](../../tests/KeyLoad.UnitTests/MessagingTests.cs); agent surface tests PLANNED після ADR-039 acceptance |

MCP і agent surface required; accepted contracts, exact names and wrappers are in
ADR-039 and [MCP acceptance](../../mcp-agent-api.acceptance.md). Implementation and
runtime qualification remain pending. SDK bearer/API key не містить trusted roles;
credentials/authorization — [Authorization](Authorization.md). Every operation має
окремий Orleans request grain, тоді node-local host виконує операцію;
[ADR-020](../ADR/ADR-020-independent-query-contexts.md) не дозволяє per-client
head-of-line blocking або storage ownership у session facade. Native stateless
transport does not capture a session principal. The .NET SDK's missing Backup and
SetDispatch are explicit parity work, not completed methods.

Canonical map: Client/Server `Features/ClientApi/` transport/adapters та matching IntegrationTests/UnitTests helpers; business operations/tests зберігають owning slice name. CLI — operator/worker entry point; окремий frontend N/A. Shared contracts і host composition мають одного integration owner. Freeze protocol → real parity tests → adapters → rollout/version contract → exact GitHub TUnit/recovery/Docker RF3 evidence. Existing source/test names не виконують planned MCP/agent AC; timeout не означає rollback.
