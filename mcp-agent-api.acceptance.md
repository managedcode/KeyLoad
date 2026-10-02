# MCP and agent API acceptance

Source of truth: [ClientApi](docs/Features/ClientApi.md),
[ADR-039](docs/ADR/ADR-039-official-mcp-agent-api.md),
[Authorization](docs/Features/Authorization.md),
[ResourceExecution](docs/Features/ResourceExecution.md),
[BlobStorage](docs/Features/BlobStorage.md) and the
[native foundation acceptance](docs/implementation/orleans-foundation.acceptance.md).

Actors are an authenticated application, AI agent and durable worker. The entry
point is the official SDK's stateless Streamable HTTP endpoint `/mcp`. Existing
HTTP callers retain their typed protocol. Both adapters dispatch through a shared
signed Orleans operation gateway; tool arguments cannot provide principal IDs,
roles, evaluation timestamps or internal membership authority.

| Acceptance | Measurable result | Required genuine proof |
|---|---|---|
| AC-MCP-001 / AC-CLIENT-006 | Exactly the 37 implemented public capabilities are discoverable; Authenticate and Membership are absent. Each catalog entry declares canonical input/output schema, the canonical route used by the sole HTTP governor to choose its admission class, and retry behavior. | Catalog/HTTP/SDK parity checks and official MCP ListTools/CallTool calls against Docker/Aspire RF3; all ten batch mutation variants remain represented. |
| AC-MCP-002 / AC-CLIENT-006 | Persisted credentials are resolved on every HTTP request; revoked/expired keys and tenant/field denial cannot reuse discovery authority. Each execution uses a fresh request GUID and retains the caller's stable command ID. | Concurrent official SDK callers with distinct keys, revocation after discovery, cross-tenant denial, genuine RF3 failover and response request-ID observations. |
| AC-MCP-003 / AC-CLIENT-006 | Outer argument names, required fields, enums, polymorphic discriminators, immutable-array null/default bounds and binary base64 forms are strict. Unknown tools never execute an operation. | Official SDK malformed/oversized/Unicode/array/base64/AST and mutation cases plus exact canonical schema/JSON regressions in CI. |
| AC-MCP-004 / AC-CLIENT-007 | Wire parsing is bounded before allocation. After tool classification, the same existing HTTP lane and working-set reservation apply before typed arguments are decoded. Data saturation does not consume the control execution reserve. | Real held SDK requests saturate the data lane while genuine MCP delivery/dispatch succeeds; heavy-read budgets and released/cancelled leases are observed through existing diagnostics. No fake handlers or governor replacements. |
| AC-MCP-005 / AC-CLIENT-005 | A cancelled/disconnected accepted write does not imply rollback. Same command ID/content has one outcome; conflicting content fails. Domain errors expose safe Problems, not credentials, payloads or exception objects. | Actual client cancellation/lost reply/retry, same-command RF3 failover, conflict and bounded error cases; inspect CI artifacts for secret leakage. |
| AC-MCP-006 / AC-CLIENT-004 | Backup and SetDispatch are available from the real .NET SDK as well as MCP. Backup is explicitly a physical side effect even though it uses a read-dispatch capability. | SDK/MCP admin success and non-admin denial, bounded archive receipt, stable dispatch retry. A read-only tool annotation cannot be inferred solely from GrainReadKind. |
| AC-MCP-007 / AC-CLIENT-007 | Tools return canonical structured content with a short bounded text summary, documented output schema and bounded total JSON-RPC output. The simple agent interface is the same versioned tool catalog. | Official client structured-result decoding and pagination/bounds tests; no second business engine or advertised unsupported tool. |
| AC-MCP-008 / AC-CLIENT-006; AC-BLOB-001–004 | Blob operations/resources are advertised only after ADR-038 canonical implementation, authorization, chunk/range and durability contracts are accepted and joined. | Genuine large upload, commit, bounded range, delete, interruption and RF3 recovery through both clients. This item remains pending until that owning slice is implemented. |

Test methodology: TUnit on Microsoft.Testing.Platform, exclusively in GitHub
Actions. Tests use actual Kestrel, Orleans, ZoneTree, filesystem, Docker/Aspire and
the official MCP C# client. No in-memory/single-node substitution qualifies RF3.
Development restore/build/format/schema inspection is source evidence only.
Record exact source SHA, workflow/job URLs, suite counts and artifacts. An empty,
skipped or prerequisite-blocked suite is not a passing acceptance item.

Required traceability: catalog/schema cases under UnitTests/Features/ClientApi;
real caller cases under IntegrationTests/Features/ClientApi and matching owning
business slices. Every test names its AC and real dependency; existing assertions
and process contracts must remain. Coverage and code-size gates remain mandatory.

Current result: contract accepted for implementation; no MCP runtime qualification
has been recorded. Native foundation, blob implementation, all-operation caller
parity and stable-main delivery remain open.

Transport refinements for AC-MCP-002/003/005/007: missing, revoked or expired
credentials fail before the native SDK with HTTP 401, a safe canonical Problem
and no execution GUID. Application errors after dispatch use the tool error
wrapper with its actual GUID. A present routing header with a missing body field
must receive fixed Validation rather than an SDK response reflecting that header.
Inbound JSON-RPC string identifiers are limited to 256 encoded bytes as resource
framing; the native SDK still interprets their protocol type. Canonical reply
values have at most 61 container levels so the wrapper, CallToolResult and native
JSON-RPC envelope fit the existing depth 64. Bounded actual native serialization
before SSE verifies the complete outgoing byte ceiling, including metadata.
