# MCP and agent API brainstorm

This is a non-trivial protocol/security/resource change. The owner requires the
official MCP C# SDK, all database operations, persisted authorization and real
Docker/Aspire clients. Current official SDK 2.2.0 and native net10.0 packages were
reviewed at tag commit 6fa3825973949a9c4f0cd8af344e15a8db09dc35. This is SDK evidence,
not evidence that KeyLoad already implements the adapter.

Chosen: one explicit typed tool catalog and stateless Streamable HTTP `/mcp` in
the existing Server. HTTP and MCP share a signed operation gateway and owning
feature engine. Explicit outer argument/schema checks avoid accidental assembly
discovery. Current-principal binding happens per HTTP request, and the operation
grain reloads persisted grants before effects. Caller command IDs remain separate
from MCP request IDs and Orleans execution IDs.

Rejected: a second MCP business engine; trusted roles from model arguments;
session-captured principals; unconditional RequireAuthorization without a real
authentication scheme; reflection discovery mixed with an explicit full catalog;
and treating every GrainReadKind tool as free of side effects.

Resource design: a bounded MCP ingress lease covers raw JSON-RPC framing and
native SDK parsing. A classified tool then acquires the existing HTTP governor
using its canonical route, including the existing reserved control lane and
heavy-read working-set size, before canonical DTO decoding. The ingress lease
is released only after the operation lease replaces its retained-body reservation.
It is not held while database execution waits. Independent ingress saturation
may reject all callers; data execution saturation must not occupy the control
reserve. No alternate command governor or admission bypass is introduced.

Output design: exact canonical structured JSON with a short text summary. Native
DTO conversion's duplicate full text JSON is avoided. Domain failures return
IsError and a safe canonical Problem; bounds include envelope/base64 expansion.
SDK JSON Trace and raw-exception logging are disabled for the SDK category;
KeyLoad emits only safe codes, request IDs and exception type names when needed.

Parallel work: SDK/source and admission preflight can be reviewed independently;
catalog/schema source and caller test source can follow frozen signatures. Root
alone owns shared package/host/middleware/gateway files. Blob operations are a
separate canonical feature and join only after ADR-038 acceptance. RF3 fixtures
and native lifecycle retain their current ownership and pending qualification.

Unknowns that require review before adapter writes: the exact bounded ingress
handoff and schema handling of custom readonly converters. Runtime rollout stays
blocked until those reviews, mandatory checks and genuine CI acceptance pass.
