# MCP and agent API execution plan

Objective: implement the accepted ADR-039 using the official SDK 2.2.0, every
existing public operation and genuine SDK clients, then join accepted blob
operations. [Acceptance](../../mcp-agent-api.acceptance.md) and
[brainstorm](../../mcp-agent-api.brainstorm.md) precede source changes.

The canonical catalog has one tool per typed capability. For body-bearing tools,
the only outer argument is `request`; the four header-identity command adapters
also require `commandId` (ConfigureResource, ConfigurePrincipal, ConfigureApiKey,
SetDispatch). No-body reads accept an empty object. Receive/ReceiveSubscription
retain their request.RequestId as the stable write identity. Other commands keep
request.CommandId. Dispatch's request is the canonical boolean. Public protocol
version is 1, tool names are stable `keyload_<feature>_<operation>` names frozen
in the catalog table in ADR-039. No internal principal/membership tools exist.

| Task | REQ/AC | Owner / model / permissions | Dependencies and start condition | Artifacts / verification / join | State |
|---|---|---|---|---|---|
| TASK-MCP-CONTRACT | CLIENT-004–007; AC-MCP-001–008 | Root; strongest inherited reasoning; docs/shared source only | Completed official SDK preflight and existing 37-operation inventory | Accepted ADR/catalog, acceptance, bounded admission/error/schema contract; preserve HTTP wire and feature ownership | contract source accepted; admission/schema implementation review pending |
| TASK-MCP-ADMISSION-REVIEW | AC-MCP-003/004/005 | Durable reviewer; read-only Core/Server/native SDK sources | Two-stage ingress/canonical-lane design and actual public governors | Exact safe handoff, retained-byte accounting, cancellation/log/error findings and SDK filter hooks; root resolves before implementation | complete; P1 frame/DOM/serialization/early-header findings accepted and resolved by the implementation contract below; runtime bounds remain CI pending |
| TASK-MCP-SCHEMA-REVIEW | AC-MCP-001/003/007 | SDK reviewer; read-only canonical DTO/converter and SDK schemas | Frozen explicit catalog direction and current JsonDefaults | Schema strategy for enums, arrays, bytes, polymorphism, nullable/required fields without contract forks; source-backed API signatures and strict validation plan | complete; native schema-only projection, exact enum forms, nullable metadata and wrapper refs accepted below |
| TASK-MCP-CATALOG | AC-MCP-001/003/006/007 | SDK worker; only new Server Features/ClientApi catalog/schema files and new UnitTests ClientApi catalog/schema suites | Root resolves review findings and freezes factory/gateway signatures | Complete 37-operation immutable catalog, exact input/output schemas and typed decoding; tests-first canonical regressions; no package/middleware/client edits | approved; source pending |
| TASK-MCP-FRAMING-TESTS | AC-MCP-003/004/005 | TUnit worker; only new UnitTests ClientApi frame and memory-budget suites | Frozen actual framing and memory-only reservation signatures below | Genuine hostile UTF8/JSON and lease/cancellation/growth cases before implementation, no fake SDK or HTTP handlers; root integrates source and CI | approved; source pending |
| TASK-MCP-MEMORY | AC-MCP-004/005 | Durable worker; only new Server ClientApi McpMemoryBudget/McpMemoryLease/McpMemoryLane files | Frozen memory-only handoff and tests-first framing worker source | Atomic independent lane reservations/growth/idempotent release, original reservation retained after failure; no command counters, principal, host/config or middleware changes | approved; source pending |
| TASK-MCP-BODY-TESTS | AC-MCP-003/004/005 | TUnit worker; one new UnitTests ClientApi real-file body-buffer suite only | Frozen bounded replay/ownership signature below | Real filesystem declared/chunked-equivalent bounded reads, mismatched/overrun input, cancellation and private-byte zeroing; source before root implementation, no fake streams | approved; source pending |
| TASK-MCP-JOIN-REVIEW | AC-MCP-002–005 | Durable reviewer; read-only gateway/framing/body and new test sources | Reviewed source joins | Independent exact actor/auth/header/framing/depth/ownership/cancellation findings before host integration; root fixes and qualifies in CI | approved; review pending |
| TASK-MCP-TRANSPORT-GUARD-TESTS | AC-MCP-002/003/004 | Durable worker; new UnitTests ClientApi transport-guard files only | Frozen current revision and bounded field-comparison contract below | Real native HeaderDictionary plus exact hostile UTF8 frames; safe header/meta mismatch classifications before SDK parsing, tests first, no HTTP or SDK doubles | approved; source pending |
| TASK-MCP-BOUNDED-JSON | AC-MCP-003/004/007 | SDK worker; new Server ClientApi bounded-JSON writer and new UnitTests ClientApi suites only | Frozen canonical streaming contract below | Tests-first actual JsonSerializer parity, exact byte bounds and private-buffer cleanup; owned native-stream implementation, no decoder/config edits | approved; source pending |
| TASK-MCP-MEMORY-PROJECTION | AC-MCP-004/005/007 | TUnit worker; new Server ClientApi projection helpers and new UnitTests ClientApi suites only | Accepted named-component equations below | Tests-first checked component accounting and boundary guards; no counters/config/session edits; root verifies join and qualifies genuine native allocations in CI | approved; source pending |
| TASK-MCP-CALLER-BASELINE | AC-MCP-001/002/003/005/007 | SDK worker; new IntegrationTests ClientApi helper/discovery tests and new DocumentStorage MCP parity tests only | Joined source fixture, frozen native endpoint/catalog/wrappers | Actual official HttpClientTransport and MCP client, complete discovery, persisted credential revocation/denial, stable document retry/conflict and execution identity; source first, no local runtime or fixture edits | approved; source pending |
| TASK-MCP-GATEWAY | AC-MCP-002/004/005/007 | Root; Server gateway/middleware/host and central packages | Accepted reviews, tests-first source and joined catalog | Shared canonical Orleans gateway, bounded ingress-to-operation handoff, safe replies and official stateless transport; strict integrated build/static checks | pending |
| TASK-MCP-SDK-PARITY | AC-MCP-006 | Root; new SDK feature helpers plus smallest explicit facade change | Existing Client owner source join complete; public signatures frozen | BackupAsync and SetDispatchAsync with unchanged canonical routes, keys and stable command identity; real client parity CI | pending |
| TASK-MCP-CALLERS | AC-MCP-001–007; TEST-002 | TUnit worker; only new IntegrationTests Features/ClientApi files | Joined real Docker fixture; frozen tool names, SDK signatures and output contract | Official native client transport, complete genuine operation/denial/retry/budget/RF3 scenarios, no doubles or local runtime tests | pending |
| TASK-MCP-BLOBS | AC-MCP-008; BLOB-001–004 | Separate owning blob worker after accepted ADR-038; shared join root-owned | Canonical blob contracts/storage/SDK implemented and reviewed | Catalog plus opaque authorized resources, native FromBytes/DecodedData bounded ranges and genuine large RF3 flow | pending |
| TASK-MCP-QUALIFY | all MCP ACs | Root with independent strong review | Complete reviewed source; native build/format/governance/coverage gates clean | Exact validation-ref GitHub Actions SHA/jobs/artifacts, every all-operation and fault gate; stable main only after reviewed stable result | pending |
| TASK-MCP-EVENT-PARITY | REQ/AC-EVENT-004/005/006; AC-MCP-002/005/007 | mcp_eventstreams, gpt-6-luna high; only new IntegrationTests Features/EventStreams test/scenario/tokens/assertion files | Lead accepted unchanged canonical event/batch/read/authority contract in acceptance and ADR-039; lead resource-scoped identity overload available | Real SDK/MCP append stable retry, identical limited/continued/empty pages, distinct execution IDs, cross-tenant denial, invalid limit/stale generation and safe healthy follow-up; no local runtime or fixture edits. Lead diff/build/format then complete GitHub RF3 evidence | ready for source implementation; runtime qualification pending |

Root serializes integrated restore/build and central edits. Workers do not launch
tests, helpers, AppHost or containers locally. Independent quality/comparison work
is preserved; prerequisite failure is not permission to suppress it or use stale
DLLs. Source/extraction review does not qualify runtime behavior. Changed tests
must preserve assertions and use actual dependencies. Coverage/complexity and all
native acceptance gates remain required for delivery.

Central pins: ModelContextProtocol.AspNetCore 2.2.0 in Server and
ModelContextProtocol.Core 2.2.0 in the official integration client. Restore must
resolve the SDK's current Microsoft.Extensions dependencies without downgrade;
never silently constrain an incompatible transitive version or replace the SDK.

## Reviewed schema handoff

Factories are `Read<TRequest,TResult>(name,route,kind,nullableResult=false)`,
`Read<TResult>(name,route,kind,nullableResult=false)`,
`Command<TRequest,TResult>(name,route,kind,Func<TRequest,Guid> commandId)` and
`HeaderCommand<TRequest,TResult>(name,route,kind)`. They return immutable internal
McpOperationDescriptor records: Name, Route, exactly one ReadKind/CommandKind,
Description, InputSchema, OutputSchema, explicit ReadOnly/Idempotent/Destructive
hints and typed Decode(IDictionary<string,JsonElement>?,int maximumPayloadBytes) delegate. Decode never
mutates native arguments. Its McpDecodedOperation result carries the kinds,
stable CommandId and owned ReadOnlyMemory<byte> Payload only. It cannot contain
trusted principal or evaluation time. Empty arguments are accepted only by the
four no-body reads. Reject unknown/missing keys and empty command IDs explicitly.

McpOperationCatalog.Entries is ImmutableArray<McpOperationDescriptor>; its ordinal
FrozenDictionary lookup provides TryGet. Fresh native SDK Tool/Annotations objects
are created from descriptors on discovery. Schema elements are owned and immutable
for catalog lifetime; there is no mutable singleton SDK ToolCollection.

Copy canonical JsonDefaults.Options privately for schema metadata only. Remove
exactly the registered strict ROM<byte>/ImmutableArray converter families by
CanConvert probes; require one match each. Retain other converters/options/resolver
and MakeReadOnly(populateMissingResolver:true). Native converter traversal then
preserves refs/polymorphism/required/default/null metadata. Never use this copy for
runtime decode/write. Native exporter hooks inspect Nullable underlying types,
annotate actual base64, and add the canonical enum numeric forms without changing
runtime handling: input is string or underlying integer with declared examples;
output is the native string branch or underlying integer. Do not narrow canonical
numeric strings/case/whitespace/comma parsing or flags.

Export each full DTO graph once, store it under root $defs.request/$defs.result,
and rebase only schema-keyword local refs. Never alter user-data default/example
$ref properties. Input and wrapped output are closed root objects; arbitrary
JsonElement values stay intentionally open. Safe error schema has only canonical
Problem converter fields type/title/status/detail/errorCode, never opaque
extensions or a fabricated statusCode field.

## Reviewed framing, memory and lifetime handoff

McpFrameBounds.Inspect(ReadOnlySpan<byte>,int maximumBytes) returns internal
McpFrameShape(TokenCount,PropertyCount). It validates strict UTF8/JSON framing,
one object, duplicate property names, depth64, at most131072 tokens and32768
properties, and at most256 encoded bytes per property name. All invalid framing
uses fixed safe Validation detail; structural/byte exhaustion uses
ResourceExhausted. It never implements MCP methods, schemas or business decoding.
This resource preflight happens before native SDK DOM parsing, from one bounded
private body buffer; native SDK remains the protocol implementation. Wire framing,
whitespace and metadata count toward existing per-tool body ceilings. Replay only
the checked bytes to the official SDK. No unbounded stream read or full-result
text duplication is permitted.

McpMemoryBudget(long dataBytes,long controlBytes,long ingressBytes) is an internal
memory-only owner. Reserve(McpMemoryLane,long,CancellationToken) returns an
idempotent lease; GrowTo(long,CancellationToken) atomically fails before mutation
on exhaustion or cancellation. Lanes are Data, Control and Ingress, with independent
byte pools. No principal, command or execution quota counters exist here; the
canonical HttpAdmissionGovernor remains sole execution admission authority.
Negative sizes, shrink attempts and use-after-dispose fail explicitly. A growth
failure retains the original lease so a fixed safe-error allowance still exists.

The ingress reservation covers the bounded body buffer, native UTF8 ownership,
bounded token metadata/property materialization and parse temporaries. Pool/copy
headroom must be named in code and retained accounting, not presented as an exact
measured heap ceiling. Canonical/supplemental reservations replace this coverage
before ingress releases. Incoming native filters classify raw tools/call before
typed CallToolRequestParams allocation, call existing Begin(route,actual full
wireBytes), Bind(current principal), and reserve the memory supplement. Control
frames fit MaxControlBodyBytes; control replies have an explicit64KiB ceiling.
Data replies retain the canonical16MiB ceiling. Unknown/invalid/admission-failed
tools never execute; handlers only format a bounded safe rejection.

Before execution, cover the existing maximum canonical reply and a small safe-error
allowance. After the actual reply bytes arrive, atomically grow the supplement
using the checked output token/property shape and bounded native envelope buffers,
before creating an owned result JsonDocument. Do not Clone its JsonElement. Hold
that document, body buffer and all operation/supplement leases in the per-HTTP
state until middleware await-next plus native stateless session draining completes.
The handler's return is earlier than SDK SerializeToNode/SSE writes and cannot
release this ownership. Cancellation never signals rollback of an accepted write.

ConfigureSessionOptions adds closures only to its fresh per-request McpServerOptions;
do not assign shared Handlers/Filters instances. Register IHttpContextAccessor,
but do not capture an initialize/session principal. Prevalidate bounded protocol
headers/metadata with fixed safe failures before SDK early HTTP errors could echo
attacker values. Disable the SDK logger prefix; safe KeyLoad diagnostics contain
codes/IDs/type names only. Shared ingress exhaustion fails closed for all callers;
the progress guarantee covers execution data-lane saturation, not slow-body ingress
exhaustion. Exact observed memory/latency and all genuine fault evidence remain CI
qualification, not asserted from source or planning reservations.

Shared gateway handoff: CanonicalOperationGateway.ExecuteAsync(HttpContext,
GrainReadKind?,OperationKind?,Guid commandId,ReadOnlyMemory<byte>,CancellationToken)
returns DispatchedOperationReply(RequestId,Payload). It resolves only the current
middleware principal, creates/signs a fresh operation GUID, records that GUID in
request-local Items and routes through OrleansNode. It preserves the HTTP request
header before execution where headers are still writable; MCP's structured wrapper
remains authoritative after native SSE headers have started. RequestId(context)
returns null until execution starts, allowing safe predispatch failures. Existing
real RequestIdReceiptTests are the tests-first HTTP identity baseline; official MCP
caller tests add the second adapter rather than a substituted gateway dependency.

Bounded body handoff: McpFrameBody.ReadAsync(Stream,long? declaredLength,
int maximumBytes,CancellationToken) returns an owned IDisposable body with
WireBytes, Shape and a borrowed ReadOnlyMemory<byte> Bytes property. OpenReader()
rewinds and returns its owned replay stream; native SDK alone interprets protocol
messages. Validate declared lengths before reading; read at most maximumBytes+1
to detect overrun, never append the extra byte to the retained buffer. Require a
present declaration to equal actual bytes. Invalid framing/mismatch uses fixed
Validation; overrun uses ResourceExhausted. Cancellation preserves the original
exception and clears private scratch/body bytes. Dispose zeroes the full owned
buffer capacity before closing it, is idempotent, and makes later access fail.
This body and replay stream remain HTTP-state owned until native draining finishes;
callers cannot retain the borrowed bytes past disposal. Actual FileStreams in CI
cover this handoff before real Kestrel/MCP range/transfer scenarios qualify it.

Each OpenReader call creates a read-only view over the same private bytes and
closes the preceding view. Disposing a view does not close the buffer owner;
owner disposal closes the current view and zeroes the full private capacity.
The SDK never receives a writable stream or access to its underlying array.
The buffer has one fixed capacity: the checked declared length, or the configured
maximum when the length is unknown. No growth reallocations leave discarded
private wire arrays behind. A present declaration permits one extra detection
byte without appending it; surplus within the global limit is a length mismatch,
while crossing the global byte limit is exhaustion. Ingress accounting covers
this entire capacity, the detection scratch and native preflight overhead.

Framing review follow-up: reject escaped lone UTF-16 surrogates in both property
names and string values with the same fixed Validation error. Handle only the
reader string-decoding InvalidOperationException at that narrow boundary; a broad
consumed catch would hide unrelated defects. Valid surrogate pairs remain valid.
McpFrameBounds.InspectValue(ReadOnlySpan<byte>,int maximumBytes) applies the same
UTF-8, token, property, depth, duplicate and byte ceilings to one arbitrary JSON
value, permitting canonical arrays, scalars and null before wrapping the result.
Inspect still requires one object for actual inbound protocol frames. Tests-first
cases for both refinements precede implementation; no schema/business validation
is moved into this resource preflight.

## Reviewed transport boundary

This new endpoint is configured with the SDK's current revision 2026-07-28 and
stateless native sessions. It does not add historical initialize compatibility.
The official current client uses native server/discover plus per-request metadata.
MCP-Protocol-Version must be one exact 2026-07-28 value. Mcp-Session-Id and
Last-Event-ID are unsupported here. Names are explicit owned wire-boundary constants
because the SDK's McpHttpHeaders/McpProtocolVersions enclosing types are internal;
there is no reflection, copied negotiation engine or SDK fork.

McpTransportGuard.ReadHeaders(IHeaderDictionary) returns immutable
McpTransportHeaders(Method,Name), each nullable when absent so the native SDK still
owns its standard missing-header reply. Present values must be a single header,
trimmed, visible ASCII or tab, with Method at most 64 characters and Name at most
256. Present protocol, session, length/character and multiple-value failures use
fixed Validation detail. No incoming values appear in diagnostics or failure text.
McpTransportGuard.Inspect(ReadOnlyMemory<byte>,McpTransportHeaders) accepts an
already framing-checked object buffer, uses one temporary owned native JsonDocument
under the ingress reservation, and closes it before SDK dispatch. It compares only
present HTTP method/name fields and body protocol metadata that otherwise appear
in native early mismatch responses. Use JsonElement.ValueEquals for field matching,
without allocating unbounded field strings. Both params.protocolVersion and
params._meta[MetaKeys.ProtocolVersion], when present, must equal the configured
revision. The tool/prompt name or resource URI must match a present Name header;
missing mandatory fields/capabilities, JSON-RPC IDs and method implementation remain
the official SDK's responsibility. Arbitrary business request values are untouched.
An absent Method header does not prevent matching a present Name against the body
method's native name/URI field. Unsupported session/Last-Event-ID keys are rejected
by presence, even if the values are empty.

Root owns product guard and composition. Tests first use genuine native
HeaderDictionary and serialized bytes for exact revision, trimming, absent routing
headers, extra header values, invalid/bounded ASCII, session rejection, method/name
and metadata mismatch, marker-free failures and unchanged business payload bytes.
Integration then verifies actual Kestrel transport with the official client. These
unit objects are concrete production data types, never fake HttpContext/transports.

## Reviewed canonical input serialization bound

McpBoundedJson.Serialize<T>(T value,int maximumBytes) returns owned canonical UTF-8
using the actual JsonDefaults.Options and native JsonSerializer.Serialize(Stream).
Its private write-only stream has one fixed capacity and rejects a write before
appending beyond the limit. Clear the entire private capacity on success or
failure after copying only the actual accepted bytes to the returned owner. No
custom JSON format, converter or business engine is introduced. Positive limits
are required; overflow is ResourceExhausted with a fixed safe detail. Native
serializer buffering/escaping temporaries still need their named pre-reservation.

The descriptor Decode receives an optional maximumPayloadBytes, defaulting to the
existing Kestrel 32 MiB ceiling for isolated canonical tests. The MCP gateway always
passes the actual canonical lane ceiling. Its immutable decoder delegate accepts
that limit; it cannot read HttpContext, principal, evaluation time or ambient state.
No-body null payloads are checked too. This prevents canonical defaults or escaping
from creating an unbounded owned serialized payload. Pre-decode memory projection
also includes the largest native serializer/escaping hint up to six times complete
wire bytes plus 64 KiB DTO-default headroom. This is a pinned-format budget convention,
not an exact measured heap assertion. Actual canonical output is rejected at the
lane byte ceiling before dispatch when it does not fit.

Tests compare actual UTF-8 with JsonDefaults.Serialize for real canonical DTOs,
Unicode/escaped strings, enum forms and empty values; test exact byte limits,
overrun, positive-limit guards and failed-write cleanup using the production native
writer rather than doubles. Integrated growth/discovery/SSE evidence remains CI.

## Reviewed retained-memory component accounting

The projection uses checked long arithmetic and independent byte pools; it never
changes HTTP admission counters. Defaults are data 2 GiB, control 128 MiB and
unclassified ingress 1 GiB, configurable as three positive node-local byte limits.
These are retained-allocation budget conventions. Catalog retention, returned pool
arrays, GC delay, Kestrel/TLS and process headroom require separate CI observations;
the formulas are not exact managed-heap measurements or guaranteed process RSS.

For the pinned runtime, Q(n) rounds to the next power-of-two pool bucket, minimum
16. Named conservative components are:

- Metadata(n,t) = 2*Q(max(n+12,12*(t+1))) + 12*t + 4096 for initial metadata,
  growth/trim overlap, owned completion and parse stack.
- Writer(n,h) = 2*Q(2*n+h+256) for growth, maximum size hint and old/new overlap.
- Escaping(n) = Q(n) + 2*Q(6*n+256) for concurrent unescape/escape and UTF-16 rent.
- Structure(t,p) = 128*t + 128*p + 256*64 for native values/containers,
  dictionaries/name sets and depth slots; add 2*n for decoded UTF-16 content.
- Input expansion hint = 6*wireBytes + 65536 for native writer escaping/defaults.
- Native envelope allowance = 65536 bytes and 1024 tokens/properties of fixed
  transport/metadata headroom. Native output encoding and total byte bounds must
  still be checked by root; this headroom is not a proof of an encoder expansion.

McpMemoryProjection.Ingress(int frameCapacity) precharges the complete private
capacity plus 16384 scratch, worst bounded framing/native-untyped/guard-document
components, maximum 16 MiB owned authentication reply, and a 256 KiB fixed safe
failure allowance. Worst shape uses min(capacity+1,131072) tokens and
min(capacity/4,32768) properties. Authentication reply framing is checked before
principal decoding with the same depth/token/property/name ceilings and 16 MiB
byte ceiling. Authentication(capacity,authBytes,authShape) returns the ingress
charge plus actual metadata/structure/string decoding headroom; grow before
JsonDefaults.Deserialize<PrincipalRecord>. Keep cancellation and database authority
unchanged; HTTP and MCP share the same canonical authentication read dispatcher.

McpInputMemory(Capacity,WireBytes,Shape,MaximumPayloadBytes,AuthenticationBytes,
AuthenticationShape) is immutable allocation input only. BeforeOperation(input,
maximumReplyBytes,bool protocolReply) charges complete retained body capacity,
native-untyped parse/metadata/structure, native typed-parameter writer/documents,
canonical DTO/serializer/escaping peak, full fixed canonical write capacity plus
returned-copy overlap, actual principal retention, maximum canonical reply and
safe failure allowance. Do not credit the HTTP database working-set reservation
against these allocations. For protocol replies whose built-in native handler
cannot expose a pre-SerializeToNode hook, also precharge the complete bounded
control reply peak before calling native next.

AfterReply(long held,int replyBytes,McpFrameShape replyShape) returns held plus
the actual bounded wrapper/native-reply/SSE peak: owned wrapper capacity,
wrapper document metadata, SDK SerializeToNode writer and owned UTF8/metadata,
native value/string structure, two separate native SSE writers and escaping
temporaries. It must never shrink the original reservation. All sizes/counts are
nonnegative and within the approved byte/shape ceilings; reject invalid arguments
before arithmetic. Round/growth overflow cannot wrap into a smaller reservation.
Root acquires canonical and supplemental ownership before releasing ingress,
grows before output DOM allocation, and releases only after actual SDK draining.

Projection unit cases verify real boundary arithmetic, named metadata/writer
overlap, shape sensitivity, zero/negative/overflow guards and monotonically larger
reply growth. They do not pretend to measure native allocations. Genuine maximal
token/string/escaping, discovery, slow SSE and control progress gates remain CI.

## Reviewed native response and credential boundaries

Credential failure occurs before native protocol processing: HTTP 401 with the
safe canonical Problem, no operation GUID and no tool envelope. A previously
successful discovery cannot preserve that authority. A current principal denied
by a dispatched capability receives the safe IsError wrapper and actual GUID.

A present Method with no body method and a present Name for tools/call,
prompts/get or resources/read with absent/non-object params or missing target
are fixed Validation failures. Absent routing headers retain native missing-header
validation. This closes the SDK's early reflected-header mismatch path.

McpFrameShape gains optional Depth=0 to preserve count-only callers. Inspection
records maximum container depth. McpFrameBounds.InspectReply checks the same
arbitrary-value framing plus depth at most 61 before result ownership is allocated.
The three outer objects then fit native/client depth 64. Inbound root string id
has at most 256 encoded bytes; UTF8 framing inspects this resource length without
implementing the native identifier type/protocol. Scalar IDs stay native-owned.

Root installs fresh per-request handlers and message filters. The public native
MessageContext.Server.SendMessageAsync(new JsonRpcResponse{Id,Result},ct) is the
early tools/call rejection seam before typed parameter conversion. Never assign
message.Context; the destination-bound native server owns transport association.
The outgoing native filter sanitizes error Message/Data and bounds complete
native JsonRpcResponse serialization before SSE. The complete native frame byte
ceiling is maximumReplyBytes plus 65536, not an assumed encoder equivalence.
Use actual native serializer options and a bounded stream; memory accounting
covers this writer overlap before conversion and output. The root keeps all
owners through native stateless draining and skips native next on tool admission
failure. Caller cancellation propagates; no cancellation claims rollback.

| Task | ACs | Owner and permissions | Start condition | Artifacts / join |
|---|---|---|---|---|
| TASK-MCP-RESPONSE-FRAMING-TESTS | AC-MCP-003/005/007 | Durable worker; only new Unit ClientApi boundary files | This response/credential refinement accepted | Tests-first depth 61/62, ordinary/scalar result shape, root string id byte limits and missing-header-field safe failures; root owns product source, strict build and CI |

Source joins reviewed so far: catalog/schema, frame/lease/body tests and owners,
canonical bounded writer and transport guard. Server Release build after bounded
decoder integration passed with zero warnings/errors in development build 5.
This is source evidence only; endpoint, callers, output lifetime, blobs and all
GitHub runtime qualifications remain open.

Projection review corrections: inbound capacity and canonical payload use the
configured Kestrel ceiling 32 MiB, independently of the 16 MiB reply ceiling.
Ingress and principal decoding include their actual Escaping component before
native string unescaping. Protocol precharge uses the worst shape for a 64 KiB
control result (min(bytes+1,131072) tokens and min(bytes/4,32768) properties),
not the 1024-item envelope allowance alone. AfterReply names three full wrapper
capacities: bounded writer and returned owned wrapper overlap, plus SDK owned
UTF8 result. It then adds metadata, structure, native and both SSE writer peaks,
and escaping. Explicit counts complement the pool-growth components; a generous
unrelated component is not authority to omit a known retained owner.

| Task | ACs | Owner and permissions | Start condition | Artifacts / join |
|---|---|---|---|---|
| TASK-MCP-OUTPUT-OWNERS | AC-MCP-003/005/007 | Durable worker; new Server ClientApi output helpers, new Unit suites and only WrittenBytes property in bounded stream | Frozen native output helper contract below | Tests-first exact canonical wrappers, safe Problems, lifetime/zeroing, actual native message byte/depth checks; root owns per-request reservations, SDK handlers and final join |

McpReplyOwner.Success(ReadOnlyMemory<byte>,Guid,int maximumBytes) and
Failure(ErrorCode,Guid?,int maximumBytes) own an exact UTF8 wrapper buffer and its
JsonDocument. ToolResult() returns a fresh native CallToolResult whose borrowed
StructuredContent remains owner-held; success has short fixed text and failure
has IsError=true plus fixed safe text. Failure creates a fresh Errors.Problem
using only the code and safe owned detail, never an exception/payload message.
Dispose closes the document then zeroes the whole owned wrapper array, once.
No cloned root or duplicate complete JSON text is retained. Product caller must
grow its complete output reservation before invoking the factory.

McpNativeOutput.Validate(JsonRpcMessage,int maximumBytes) uses actual public
McpJsonUtilities.DefaultOptions with native polymorphic JsonRpcMessage streaming
serialization into McpBoundedWriteStream, then framing/depth inspection of its
borrowed WrittenBytes range; no returned full copy is needed. It runs after native
serverInfo metadata and before SSE. Safe byte exhaustion is ResourceExhausted;
the root filter replaces the message with one bounded fixed native failure under
already held safe-error space. No JsonRpcMessage.Context mutation belongs here.

Final native projection refinement: BeforeOperation additionally names the fixed
native-validation stream capacity maximumReplyBytes+65536, alongside the canonical
reply owner. AfterReply uses an encoded upper bound 6*wrapperCapacity+65536 for
native message/UTF8 metadata/structure and the two SSE writer components, with
the native writer's size hint set to that upper bound. This is conservative before
the exact outgoing byte validation, and can reject a large operation under a
configured retained-memory quota; it is not a promise that every maximal frame
fits every configured quota. No canonical/native encoder equivalence is assumed.

| Task | ACs | Owner and permissions | Start condition | Artifacts / join |
|---|---|---|---|---|
| TASK-MCP-PAGINATION | AC-MCP-001/003/007 | Durable worker; new Server ClientApi pagination helper and new Unit pagination suites only | Frozen stateless catalog and byte-paged native contract | Tests-first complete37 discovery over actual serialized pages, strict cursor and exact budget failures; root binds native ListTools handler and RF3 caller proof |

McpToolPagination.Create(string? cursor,int maximumBytes) returns an actual
ListToolsResult built from fresh native catalog Tool objects. Cursor is the
canonical invariant decimal index with the fixed `keyload-mcp-v1:` prefix;
null starts at zero, other forms/out-of-range values fail fixed Validation.
Measure each candidate page with actual native McpJsonUtilities.DefaultOptions
streaming serialization into the bounded writer. If the next tool exceeds the
inclusive byte bound, return the previous non-empty page with its next cursor.
A single tool which cannot fit fails ResourceExhausted; never silently omit it or
return an empty advancing page. No shared mutable SDK ToolCollection, schema clone,
custom MCP engine, principal snapshot or business dispatcher belongs here.

Pre-scan growth joins from independent review: the actual owned authentication
reply fits the already precharged raw 16 MiB, but its inspection must be covered
before inspection allocates strings/name sets. Add Inspection(int bytes), which
returns Metadata+Structure+Escaping at worst shape for those actual bounded bytes,
and AuthenticationScan(int capacity,int actualAuthBytes)=Ingress(capacity)+
actualAuthBytes+Inspection(actualAuthBytes). Grow ingress to at least this total
before InspectValue, then grow to at least Authentication(actual shape) before
principal decoding; never shrink a lease. ReplyScan(long held,int replyBytes)
returns held+Inspection(replyBytes), checked and bounded. After canonical reply
arrival and byte-limit validation, grow ReplyScan before the first InspectReply,
then AfterReply before owned wrapper/native allocation. Native validation's own
framing scanner is included in the encodedUpper metadata/structure/escaping peak.
Cancellation is checked before each bounded scan and before following allocation;
this does not promise interruption at every token of a synchronous bounded scan.

Pinned HTTP-header follow-up: the SDK's public McpHeaderEncoder.DecodeValue
decodes a native base64-wrapped Mcp-Name before comparison. ReadHeaders must use
that exact public decoder on the already bounded encoded field, validate the
decoded bound/characters, and compare the decoded value with the body target.
Raw-equal header/body encoding must not pass to a different native comparison and
reflected early error. Literal tool names are unchanged. Tests first cover actual
public EncodeValue/DecodeValue, decoded match, raw-equal mismatch and safe invalid
encoding without retaining the incoming marker.

Native outgoing fallback must preserve the already-injected SDK `_meta` when it
replaces a too-large JsonRpcResponse.Result. McpResponseReplacement.Apply(response,
CallToolResult) serializes only the fresh fixed safe result using native options,
detaches the existing native metadata node from the discarded result, attaches it
to the fresh result and replaces Result. It does not clone metadata, assign Context,
change the request ID or copy business content. The outgoing filter rechecks the
complete fallback bytes before native next. Tests use genuine native objects and
verify serverInfo/metadata/identity preservation and the absence of old payload.

Decoded Name may contain bounded Unicode from the exact native header encoder;
wire header remains bounded visible ASCII/tab. Reject empty/overlong/control
decoded values and malformed native encoded wrappers with fixed Validation.
Resolve credentials before protocol-header validation so unauthenticated requests
receive the canonical HTTP401 boundary even when they omit MCP headers; resource
capacity checks and ingress reservation still occur before authentication.

Native RF3 build1 found AppHost typed configuration assembly references missing:
Aspire project-resource references do not expose their output assemblies.
TASK-REP-INTEGRATE adds explicit non-resource Abstractions and Orleans references
for nameof-based configuration, preserving the three AddDockerfile resources.
This is compilation composition, not a host-process resource substitution.

| Task | ACs | Owner and permissions | Start condition | Artifacts / join |
|---|---|---|---|---|
| TASK-MCP-STRICT-UNIT-JOIN | AC-MCP-003/004/005/007 | TUnit worker; existing new Unit ClientApi Mcp suites only | Actual strict Unit build1 diagnostics | Preserve all assertions/cases while correcting null-safe TUnit calls, native collection contracts and static diagnostics; root rechecks strict full-project build |
| TASK-MCP-STRICT-CALLER-JOIN | AC-MCP-001/002/003/005/007 | SDK worker; its new22 caller files only | Actual strict Integration build1 diagnostics | Public pinned SDK-only APIs, equivalent real caller scenarios, static diagnostic repairs; root integrates real fixture findings separately |
| TASK-MCP-NATIVE-BOUNDARY-TESTS | AC-MCP-003/005/007 | Durable worker; new Unit encoded-header and metadata-replacement tests only | Accepted findings above | Tests first using actual public native encoder and JsonRpcResponse metadata, marker-free failures; root owns product corrections |

Source joins: the scoped Unit worker corrected11Mcp test files without removing
overload/null/error/cancellation coverage. The caller worker corrected only its
ProtocolVersion constant and use to the public pinned SDK API. Encoded-header and
metadata replacement tests first added17cases across11methods; root then joined
the exact public decoder and no-clone metadata transfer into the real filters.
Server development build7 passed0warnings/0errors including the actual /mcp
composition. The later Unit build2 compiled those Server refinements but stopped
at the independent in-progress BenchmarkScenarios project reference prerequisite;
Integration build2 stopped at14 AppHost static diagnostics. Both full test-project
builds, all genuine caller/runtime tests and coverage remain pending. Root's exact
source-only AppHost/native Unit repair graph is in orleans-foundation.plan.md;
these errors cannot be described as executed test failures or runtime proof.

| Task | ACs | Owner and permissions | Start condition | Artifacts / join |
|---|---|---|---|---|
| TASK-MCP-NATIVE-PIPELINE-REVIEW | AC-MCP-001/002/003/005/007 | High-capability native SDK reviewer, read-only Server ClientApi composition/filters/state/auth and official pinned public sources | Actual composed /mcp and latest header/metadata refinements | Exact native handler/filter order, per-HTTP auth/admission before typed decode, output/drain/error ownership and genuine discover/List/Call compatibility findings; no source changes/builds/tests/qualification; root resolves findings with first regressions |

This strong review scope covers ambiguous native transport lifetime and security,
so a cheaper bounded syntax worker is not sufficient. Its result cannot substitute
for the mandatory genuine RF3 caller, saturation and cancellation CI gates.

Native HTTP flush finding accepted under AC-MCP-003/005/007: the official POST
transport may flush response headers independently after250ms. HasStarted followed
by a header assignment in CanonicalOperationGateway is not atomic and can fail a
valid admitted operation before actor execution. Register one native OnStarting
callback at the identity middleware entry, before any native handler/auth/body
wait. It reads only the request-local execution GUID and writes the existing
X-KeyLoad-Request-Id header while the native header-start boundary owns mutation.
CanonicalOperationGateway records the GUID in Items but does not mutate headers.
If headers were committed before an execution GUID exists, the header is absent;
the actual tool structured requestId remains authoritative. Ordinary HTTP
operations preserve their existing header because dispatch precedes output.
No fabricated operation ID is added to authentication or pre-decode errors.

| Task | ACs / owner / exact scope | Start / tests / join |
|---|---|---|
| TASK-MCP-HEADER-START-TESTS | AC-MCP-003/005/007 and AC-ROUTE-001; high-capability native TUnit worker; only NEW Unit ClientApi OperationHeader-prefixed test/server/data files | Tests first using real loopback Kestrel/ordinary HttpClient, no handler/doubles: before-start exact GUID, no execution/no header, headers-started-before-ID body still succeeds, simultaneous independent header/body IDs; expected OperationResponseHeaders.Register(HttpContext) seam; root supplies helper/actual middleware/gateway fix then build/CI |
| TASK-MCP-HEADER-START-JOIN | Root only; new Server ClientApi OperationResponseHeaders.cs, existing CanonicalOperationGateway.cs and DatabaseIdentityMiddleware.cs | Reviewed tests-first packet and native independent finding; native OnStarting lifecycle, fixed key/format, exact existing GUID Items and server authority; preserve real RF3 HTTP/MCP tests; root actual graph/build/format and exact-SHA CI |

The test host proves the actual response lifecycle and caller-visible header/body
contract, not a fake Orleans engine or RF3 durability. Genuine shared gateway
request-ID, authorization and MCP/.NET cases remain the final integration proof.
Worker may inspect existing genuine Kestrel infrastructure but must not edit it,
launch local tests/helpers or add an API bypass. Stop on unsupported native API,
changed identifier/error contract or ownership overlap. Root reviews complete
source and captures qualification separately; no source-authored test is green.

Header handoff refinement before product implementation: HTTP Items is not a
cross-thread publication primitive. Register stores one request-local identity
holder before native entry, and captures that holder directly in the OnStarting
state. OperationResponseHeaders.Publish(HttpContext,Guid) atomically publishes one
immutable GUID reference after signing and immediately before actor dispatch.
OnStarting never reads Items concurrently with a dictionary mutation; it reads the
captured holder through Volatile.Read. CanonicalOperationGateway.RequestId reads
that same holder, keeping wrappers/errors/header correlated. No client-supplied
GUID or second operation publication is allowed. The existing internal Items key
is preserved but its private value is now the holder, not a boxed GUID. This is
not a public JSON/header/grain wire migration. Tests first use the actual Publish
seam and retain all four real Kestrel scenarios, including early-start/late-ID.

Header source join is now present: immutable identity publication uses one
Interlocked.CompareExchange reference and Volatile.Read; OnStarting captures the
holder directly. Root reviewed the tests-first packet and returned a missing
instance handler binding/failure-path listener ownership correction to its same
worker before compilation. Its four real flows remain mandatory, with explicit
nonempty GUID and actual HasStarted checks. No runtime result exists yet.

TASK-MCP-UNIT-STRICT3 is a preserving source prerequisite under AC-MCP-003/005/007:
the economical worker owns only McpFrameBodyTests.cs and ServerFailureObserverTests.cs.
Actual Unit build3 reports one untransferred file stream and synchronous disposal
inside an async method, plus nullable cancellation-task assertion/test visibility.
Dispose the actual source stream explicitly on every failure path while retaining
the intended disposed-stream error case; move only synchronous disposal into a
cohesive sync helper or use the native awaited FileStream DisposeAsync boundary.
Observe the exact nonnull cancellation task before asserting identity. No dropped
assertion, fake stream, public/test contract, dependency or production edit.
Root owns build/formatter and exact-SHA GitHub qualification; source workers do
not execute any test/helper/build/container. Preserve the source case/method map.
