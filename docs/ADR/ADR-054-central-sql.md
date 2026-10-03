# ADR-054: Central SQL over canonical database operations

Owner clarification 2026-10-03: SQL serves one composable agent database, where
logical model resources reference and use one another. [ADR-067](ADR-067-composable-agent-database.md)
adds bounded queue-to-entity-to-graph and graph-to-queue server derivation inside
the existing atomic command/CALL. This extends the initial invocation stage below;
the no-multiple-statements/no-recursive-CALL contract remains. Full declarative
composition and native clients remain mandatory under ADR-065.

Status: Accepted under owner direction2026-10-02; implementation/qualification pending.

## Decision and contracts

One server/database serves AI agents across documents, typed rows, graphs,
vectors/text search, blobs, queues and events. SQL becomes the central versioned
language. Keep Q1 SELECT and AST1 backward compatible. Add SQL operation-envelope
version1: Q1 SELECT compiles to the existing QueryRequest; bounded
`CALL exact_public_operation(@arguments)` compiles using the sole public operation
catalog's strict typed decoder and enters the same signed Orleans gateway. The
argument is an object envelope containing existing `request` and, when required,
`commandId`; no-body operations use an empty envelope. Do not reflect arbitrary
methods, compose multiple statements, or allow SQL to call itself recursively.

SQL is a language adapter, never a parallel execution/authorization engine. Its
CALL result has the actual target operation type. Generic SQL MCP discovery uses
conservative non-read-only/non-idempotent/destructive hints. HTTP/MCP SQL wrapper
reserves the existing conservative heavy-read DATA lane before deserialize;
control CALLs may be rejected when it is saturated. Direct fenced control APIs
retain the reserved control lane. Exact target-route admission is a future
ingress-transfer optimization requiring bounded preclassification and qualification.

This first stage supplies procedural SQL for all implemented capabilities;
declarative multi-model SELECT sources/JOIN/fusion are required later contracts,
not implied by the new facade. SQL never performs generic writes to event/queue
internals. Existing command IDs, unknown outcome, leases, replay, redaction,
capability checks, cancellation, deadlines and bounds remain with target owners.

```mermaid
flowchart LR
    SQL[Versioned SQL and bound arguments] --> Compile[Bounded SELECT or CALL compiler]
    Compile --> Catalog[Sole typed public operation catalog]
    Catalog --> Gateway[Persisted authority and signed gateway]
    Gateway --> Actor[Unique request grain]
    Actor --> Host[Node local PartitionHost]
    Host --> Models[Documents rows graph search events queues blobs]
```

## Implementation contract

REQ-AISQL-001/003/004 -> AC-AISQL-001/005–010 in root acceptance;
QueryExecution owns language, existing model slices own effects. Ordered tasks:
TASK-AISQL-004 freezes DTOs/grammar;006 authors strict compiler tests and compiler;
007 preserves native equality planner efficiency;008 joins endpoint, SDK, official
MCP, conservative admission and RF3 differential tests. Exact ownership is the
root plan task graph. Worker changes cannot alter shared transport or authority.

Root owns Abstractions/Features/QueryExecution/SqlOperationContracts.cs,
Server query/catalog/admission joins, Client/Features/QueryExecution/, integration
tests and docs. SQL worker owns new Server/Features/QueryExecution/Sql* compiler
helpers and matching SqlOperation* unit files. Existing DTOs/route values retain
semantics. The adapter rejects unsupported versions and invalid CALL grammar,
targets or argument envelopes before routing. SELECT/EXPLAIN grammar is validated
once by the existing authorized Q1 request actor before a scan or effect; the
adapter does not repeat parsing. Deployment is
additive; rollback removes advertisement/new route, existing Q1 remains.

Dependencies: ADR-010/012/013/014/020/035/036/039/055. Tests: strict syntax/version/
parameter/name/recursion rejection; Q1 equality; actual SDK/MCP read/write/error/
retry/permission/cancellation/admission flows on RF3; comparison measurements.
Integration lead joins all worker diffs and owns GitHub exact-SHA proof/artifacts.
No acceleration, full SQL compatibility or production claim before qualification.

Accepted TASK-AISQL-012 source integration repair, triggered by candidate
b3f93431a/run37068458582: KeyLoadClient's aggregate partial type exceeded200 code
lines after the additive blob surface; six write adapters also lacked null guards.
Keep typed blob methods source-compatible as feature-owned public extension
methods in BlobClientExtensions, invoking the same assembly-internal Send
transport with unchanged route/type/command ID/write classification. Validate
client and request before transport. No second HttpClient or retry path. Root owns
the one private-to-internal Send join in Client/KeyLoadClient.cs; one bounded worker
owns BlobClient.cs and new UnitTests/Features/BlobStorage/BlobSdkArgumentTests.cs.
First author null-argument tests without network/mocks, then refactor adapters and
use existing genuine RF3 blob lifecycle/range parity as operational regression.
Another disjoint worker removes unnecessary equality-planner nesting, retaining
all authored native point/index/security/budget tests and iterator semantics.
Root joins/reviews then repeats exact-SHA build/format/full runtime gates. This is
pre-delivery source refinement; no published binary/schema migration is claimed.

TASK-DBHP-012 preserves REQ-AISQL-003 and AC-AISQL-007 under AC-DBHP-012.
Tests run37113337508 at9e0532fdeed7c55a17f9c857f8d5a264215341e7 reports
62/63 RF3 passes: SqlRf3AdmissionTests observes one active control command after
the official MCP client receives its result. Native McpHttpPipeline owns its
admission through endpoint completion; a received result is not a server-drain
barrier. First retain this failing original, then change only that test to observe
both node and verified-scope control counts through genuine SDK admission calls.
The observation has a caller-linked five-second deadline and fifty-millisecond
polls; successful completion still requires both counts to be exactly zero.
Keep budget, rejection, unchanged command-ID reuse, actual direct-control success,
routing and three-voter assertions. SDK errors/cancellation remain failures.
One bounded worker owns only SqlRf3AdmissionTests.cs; root owns requirements,
review, build/format, main delivery and new exact-source CI RF3 evidence.
No production resource lifetime, public contract or storage migration changes;
rollback reverts the test-only observation. Source analysis cannot establish
that the native failure is repaired or that no resource leaks exist.
