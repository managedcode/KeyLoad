# ADR-065: Full SQL syntax and client protocol

TASK-SQLC-COMP-ORACLE / REQ-SQLC-003 / AC-SQLC-003C preserves AC-COMP-007 after
actualca22 RF3 report0a29cc8c3c5d29db4f1135277856af42c23a584a83a232b0358c8636b74f1ad1
retains66pass/1error. Messaging.Enqueue already canonicalizes queue JSON in
ordinal key order. Root first uses that original failing RF3 regression, then
owns only IntegrationTests DatabaseCompositionRf3Tests.cs and NEW
DatabaseCompositionRf3Payload.cs: preserve incoming noncanonical DTO JSON, compare
persisted exact bytes to an independent named-field ordinal JSON golden and
reparse full link equality. No product canonicalizer may generate the golden.
All forward/reverse/replay/rollback/error flows and genuine SDK/MCP RF3 remain.
Root source review/build/format/static precede stable delivery and real RF3;
reverse-flow and fullgate remain pending. No production/schema/migration change;
rollback both fixture files together. Independent ADR067/outbox work is preserved.

Owner clarification 2026-10-03 requires full SQL for one composable database,
not merely independent per-model calls. [DatabaseComposition](../Features/DatabaseComposition.md)
and [ADR-067](ADR-067-composable-agent-database.md) define the first bounded atomic
queue-to-entity-to-graph and graph-to-queue derivation stage through existing CALL.
This is a prerequisite building block; full declarative sources, joins and
data-modifying statements across the models remain this ADR's required outcome.

Status: Accepted staged implementation contract; full execution/native protocol
and exact-source qualification pending. Date2026-10-03. Integration owner: KeyLoad
root planning agent. Related QueryExecution/ClientApi/RelationalStorage/Search;
REQ-SQLC-001–011 and AC-SQLC-001–011 in the root sql-client-compatibility acceptance.
This extends ADR-012/054; their initial Q1/CALL stage is historical, not the full
product. Existing SQL envelope1 and generated DTO IDs remain unchanged.

## Accepted public/native test-oracle stage

TASK-SQLC-P1 / REQ-SQLC-003 / AC-SQLC-003P follows the root-reviewed R17 original
report (source366, CI37123589277/job111204377864/artifact11273574119; report SHA256
3471e334e4348ff727cf928ffdc845ec7b73a9e0de63b920262450217dc69635).
SQL-compiled versus canonical descriptor bytes pass before the invalid trailing
pre-JSON DTO comparison fails. Array/stream divergence or graph normalization
is not yet established; no product serialization change is authorized here.

Ordered contract: first-author actual same-instance native array/MemoryStream
writer parity plus received-command/recursive DTO semantic regressions; then
bounded gpt-6-luna/high worker updates only UnitTests ClientApi canonical corpus,
command/read/polymorphic tests, QueryExecution SQL compiler/comment tests and
NEW ClientApi/McpNativePayload-prefixed files. Root reviews every diff and joins
frozen integrated build/format/static gates, scoped main delivery and real
GitHub normal/scalar/recovery/RF3 originals. No mocks/packages/local execution.

The full original public JsonElement is an independent value oracle; generic
test-only native-decode delegates retain the actual DTO type, validate/deserialize
native bytes and compare every resulting JSON field/union/value. Preserve kinds,
stable IDs, dictionaries, original JSON, safe invalid/null/unknown rejections and
exact SQL-native parity against the same real canonical descriptor. Post-disposal
tests compare an actual-byte copy taken while the source document lived and then
decode the complete DTO. No-body uses actual native int-zero/correct read identity,
not JSON/null text. All existing recursive/raw JSON value assertions remain.

A same decoded instance must produce exact equal native array/stream bytes and
valid typed round trips. Any genuine failure remains failing and escalates to
its serializer owner; no fixture avoidance, dropped assertion or consumer
workaround. No public/format/storage/auth/transport migration; rollback corpus
and oracle changes together. FullSQL/native/coverage gates stay open.

```mermaid
flowchart LR
    Input[Original public typed JSON] --> Native[Actual canonical native payload]
    Native --> Typed[Validate and decode actual DTO type]
    Typed --> Value[Full independent public value equality]
    Typed --> Writers[Same instance array and stream parity]
```

## Decision and conformance target

The owner requires full SQL syntax and a client connection protocol. Root selects
PostgreSQL18 as the working target after an optional unanswered dialect question;
this records a planning choice, not explicit owner selection or delivered support.
Maintain separate syntax, semantic execution and native client gates in
[conformance inventory](../implementation/sql-client-conformance.json). No parser
or package alone satisfies the requirement. Every required PostgreSQL18 grammar/
command family needs typed execution and real differential tests; any pending
family keeps the full SQL gate false. PostgreSQL wire3.0 is the first native
client target, with explicit3.2 negotiation fixtures before advertising that
version. Current HTTP/JSON plus official MCP remain their actual transports.

[Command inventory](../implementation/sql-client-commands-postgresql18.json)
enumerates all183 entries from the official version18 command index. Family
mapping is research inference;75 entries need explicit named coverage beyond
the ADMIN catch-all. All command semantic contracts and qualification remain
pending. This layer does not replace separate expression/type/function/operator/
clause/catalog/protocol inventories or authorize provider/session choices.

TASK-SQLC-R12 [pinned parser research](../implementation/sql-parser-candidate-review.md)
compares managed SqlParserCS and native PostgreSQL parser bindings. Tagged
libpg_query18.0.0 is the strongest inspected PG18 syntax reference, a planning
inference; existing wrapper/package PG18 provenance remains unverified. No parser
is selected or installed. Syntax structures do not provide catalog/type binding,
authorized execution or client sessions. Freeze native ABI/RIDs, ownership,
resource/cancellation/drain and typed-plan contracts before implementation; every
advertised family still requires real differential execution and RF3 proof.

Official targets: [PostgreSQL18 SQL](https://www.postgresql.org/docs/18/sql.html),
[syntax](https://www.postgresql.org/docs/18/sql-syntax.html),
[protocol](https://www.postgresql.org/docs/18/protocol.html),
[flow](https://www.postgresql.org/docs/18/protocol-flow.html),
[Npgsql types](https://www.npgsql.org/doc/types/basic.html).

```mermaid
flowchart LR
    Clients[SDK MCP Native SQL clients] --> Transport[Bounded authenticated transport]
    Transport --> Compile[Versioned typed SQL compiler]
    Compile --> Gateway[Canonical signed gateway]
    Gateway --> Request[Fresh request grain]
    Request --> Host[Node local ZoneTree RF3 owner]
    Host --> Models[Linked relational and other models]
```

## Ordered implementation contract

1. TASK-SQLC-R1/R2/R3/R4: actual grammar/executor/security/client baseline and
   pinned FullTextSearch review. Root joins source hashes and real GitHub reports.
2. TASK-SQLC-I1: requirements/acceptance/plan before code; root owns Abstractions
   `Features/QueryExecution/SqlTriviaReader.cs`, `SqlTriviaState.cs`, internal
   status/constants and Abstractions.csproj friend access. No public helper or
   dependency. Scanner interface: `Read(ReadOnlySpan<char> sql, ref int offset,
   ref SqlTriviaState state, int maximumDepth)` returns `More`, `Complete`,
   `UnterminatedComment` or `DepthLimitExceeded`. Valid callers supply a bounded
   offset and positive configured depth. One call consumes at most256 characters,
   leaves the first nontrivia character untouched and retains only scalar state.
   It recognizes whitespace, `--` through CR/LF/EOF, and nested `/* */` outside
   tokens. No quoted-content handling, text/token allocation, callback, effect or
   capability. Unterminated comments map to fixed Validation; excess nesting to
   BudgetExceeded; raw SQL UTF8 bytes still include comments. Callers check their
   original budget/cancellation before and between chunks.
3. TASK-SQLC-Q1: worker owns SqlTokenizer/SqlParser and new named SqlComment unit
   files only. First-author acceptance tests; apply helper only between tokens,
   using configured MaxQueryDepth. Existing quoted/parameter/number readers and
   query AST remain authoritative; comments cannot splice identifiers, operators
   or permit another statement. Real ZoneTree differential rows/explain and
   configured token/byte/depth/cancel cases prove AC002–004.
4. TASK-SQLC-S1: separate worker owns SqlOperationSyntaxReader, only its constructor
   join in SqlOperationCompiler, and new named SqlOperationComment unit files.
   First-author canonical catalog/ID/exact payload and invalid/single-statement
   tests. Pass existing configured depth to scanner; preserve sole catalog,
   argument/envelope bounds, persisted authority and actual fresh request gateway.
5. TASK-SQLC-C1: root owns Client `Features/QueryExecution/SqlWriteClassifier.cs`,
   SqlClient and new named SqlClientOutcome tests. First-author genuine Kestrel
   unknown-write regression. Classify only current exact unquoted SELECT or
   EXPLAIN SELECT roots as reads after bounded trivia. CALL, malformed/unknown/
   oversized/deep/quoted roots and future forms are conservatively possible
   writes. Default immutable DatabaseLimits bound SDK classification only;
   they do not grant server admission. SDK may finish one bounded prefix chunk
   to retain existing pre-cancelled SELECT/EXPLAIN SELECT read outcomes; cancellation
   before a continuation chunk yields conservative possible-write classification.
   The256-character cancelled-prefix allowance includes both EXPLAIN trivia gaps,
   keywords and boundary checks; it never restarts for a later gap.
   Query/Server still check their original budget before every chunk. Preserve
   original request body/IDs; no
   client retries or claimed rollback. Future grammar expansion must derive
   effects from actual typed plans or default conservatively to possible writes.
6. TASK-SQLC-I2: root reviews every worker diff against AC and joins actual RF3
   commented SDK/official MCP SELECT/CALL flows, architecture/status/README and
   scoped stable main delivery. Exact source development build/format/static
   checks precede actual GitHub CI normal/scalar/recovery/RF3 and Benchmarks.
7. TASK-SQLC-FULL: root freezes per-stage typed grammar/plan/type/metadata/session/
   auth/transaction/read-cut/error/admission/cancellation contracts before workers
   implement remaining command families and native transport. Bounded SQL joins,
   routines, aggregates/windows and all models must execute, not merely parse.
   Resource/lifetime/fault contracts and real native client fixtures are mandatory.
8. TASK-SQLC-NATIVE-JOIN / AC-SQLC-011 is a prerequisite of final delivery when
   current HEAD864aa378 omits the restore caller join for its new two-argument
   native backup verifier. Root reviews/joins only the existing
   Storage.ZoneTree Features/BackupRestore/ZoneTreeBackupRestoreRestore.cs;
   a disjoint worker first authors BackupRestoreStagingJoinTests in UnitTests.
   Existing ADR-060 TASK-IS-R4B / AC-IS-004 and REQ-BACKUP-002 / AC-BACKUP-002
   own private staging, exact source preservation, journal/cut verification,
   new identity/paused dispatch and publication cleanup. No format change or
   omitted verification shortcut is introduced. Actual valid absent/empty-target
   positives include normalized trailing separators so private staging remains
   a sibling. Rare chmod/delete/move/recreate failure identity and metadata
   rollback remain outside this bounded join's qualification; no broad filesystem
   fault-closure claim. Every delivered NativeBackupCut negative case must pass in
   GitHub after enabled build/format. Rollback follows the native owning slice.

Each scope's exact artifacts/start/dependencies/review gates are in the working
plan. Inherited capable workers have disjoint write scopes; root alone owns
contracts/config/docs/Git. No suitable cheaper route is established for the
shared lexical/outcome boundary; workers must escalate contract drift.

TASK-SQLC-R13 / AC006/007/008 source planning confirms that
[IAtomicStore](../../src/KeyLoad.Abstractions/Storage/StorageContracts.cs) exposes
callback-scoped reads/commits and
[the gateway](../../src/KeyLoad.Server/Features/ClientApi/Execution/CanonicalOperationGateway.cs)
dispatches one signed operation through an HTTP principal. A native session must
not retain a storage view, impersonate that HTTP context or treat separate CALL
commits as one SQL transaction. Freeze typed binding and same-cut model operators,
atomic set-DML/DDL, transport-neutral verified dispatch/original settlement, then
canonical session transaction read-cut/write-set/commit authority before wire
implementation. Simple-query implicit batches, explicit BEGIN and modifying CTEs
require their actual semantics and fault tests. Proposed API shapes remain
planning only. The70-input audit binds inspected working bytes at HEAD8071148c,
including undelivered composition work; it is not committed equality or runtime
proof. Packet SHA256 `10b154a3a3786152c8e821026c4919b4efea70a1e609fac8b0f33dff2ef9fb7a`.

## Native transport and search freezes

No handshake/listener/session implementation is authorized by the lexical
contract alone. The later native implementation contract must fix version/frame/
encoding, statement/result types and OIDs, simple/extended protocol, portal/cut,
implicit/explicit transaction state, SQLSTATE, startup/catalog client probes,
pipeline/connection/reply numeric limits and original execution drain. Every
command revalidates persisted principal/policy and uses a fresh request grain.
Current SHA256 API-key verifiers are not SCRAM verifiers. Do not invent SCRAM,
trusted roles or DefaultHttpContext to impersonate the HTTP gateway. Auth/TLS/
credential migration and transport-neutral gateway admission require explicit
review and their own real invalid/revoked/failover/cancel/recovery evidence.

[ZoneTree.FullTextSearch](https://github.com/ZoneTree/ZoneTree.FullTextSearch) is
the owner's search candidate under AC010/ADR-009; no package/replacement decision
yet. Its private query language cannot satisfy full SQL. Establish native tree
ownership, atomic index publication, rebuild/checkpoint/restart/collision/scoring,
partial cancellation, quotas and physical-replica proof before integration. Real
matched read/create/update/delete and fault/performance tests determine the choice.

## Migration, rollback and qualification

Stage1 is additive grammar/internal SDK classification only: no data, serializer,
SQL envelope version, API route, package or storage-format migration. Roll back
the shared reader and all consumers together. Preserve canonical execution and
native WAL/atomic/RF3 journals. Native transport or broader AST/data migrations
need separate stage contracts before implementation and explicit recovery proof.

First-author TUnit/MTP tests from AC; no mocks/doubles. Unit real-store/Kestrel,
process recovery and Docker/Aspire RF3 actual SDK plus official MCP qualify only
in GitHub. Local source checks do not prove runtime behavior. Missing collector/
coverage, native clients, full execution and complete isolated measurements stay
pending. No power-loss, full SQL, production or acceleration claim before the
corresponding complete exact-source evidence. Manual source review and DBeaver UI
login/query evidence are explicit review exceptions, not substitutes for driver
tests or numeric coverage. Dependencies: ADR-004/009/010/012/013/014/020/022/024/
035/036/039/042/054/055/056/059; root preserves independent benchmark work.

## Accepted bounded BETWEEN stage

REQ-SQLC-006 / AC-SQLC-006A / TASK-SQLC-BETWEEN is the next additive Q1
expression stage under [SqlBetween](../Features/QueryExecution/SqlBetween.md).
The owning feature fixes the complete truth/error/budget/test and ordered
execution contract; strongest TASK-SQLC-R23 review joins before delegated code.
Status remains Accepted. [Original7d1196 CI](../implementation/sql-client-qualification-7d1196.json)
qualifies the bounded stage's44 execution rows and complete ordinary CI;
coverage, full SQL/native transport and complete performance gates remain open.

1. Review the current parser/normalizer/evaluator/candidate/permission/cursor
   sources and retained ca7 baseline. R22 corrects partial-bound truth: UNKNOWN
   AND FALSE is FALSE, NOT that is TRUE; UNKNOWN AND TRUE stays UNKNOWN. Eager
   non-null scalar mismatch errors and null-before-type checks remain current Q1.
2. First-author real grammar and ZoneTree TUnit tests, then lower unquoted
   `value [NOT] BETWEEN lower AND upper` to existing >=/<= AND and optional
   outer Negation. Existing Operand reads all three operands; consume only the
   delimiter AND inside the condition. No new AST/version/normalizer/evaluator,
   SQL coercion/collation/function/optimizer or keyword cleanup.
3. gpt-6-luna/high worker owns ONLY Query Features/QueryExecution/
   SqlExpressionParser.cs and SqlSyntax.cs, plus NEW UnitTests matching slice
   SqlBetween-prefixed files. No Git/packages/tests/build/runtime/workflows or
   other files. Root owns QueryEngine capability metadata, NEW IntegrationTests
   matching slice SqlRf3BetweenTests and optional helper, docs/Git/integration.
4. Root reviews all diffs against independent literal and hand-built-AST oracles,
   correct inclusive/reversed/null/missing/typed errors, quoted operators,
   configured raw/token/normalized-byte/expanded7/8node and3/4depth budgets,
   permission checks on every operand, cancellation/recovery and cursor parity.
   Strongest independent review is required before frozen full solution source
   build, formatter, static governance and scoped stable delivery.
5. Actual GitHub normal/scalar/recovery/RF3/analyzer qualification retains source/
   run/attempt/job/ZIP/report/case identities. RF3 uses genuine SDK and official
   MCP, independent positive/negative/null truth results, manifest and error/next
   request. No skipped suite or development build satisfies execution.

No persisted data, native wire, serializer or public DTO migration. Rollback
parser/manifest/new fixtures together; existing old AST/operators remain valid.
Dependencies are existing Q1 and ADR012/054, typed AST/scalar comparison, resource
limits, field authorization and SDK/MCP RF3 infrastructure. New performance
claims require actual matched scale cohorts; this stage does not add an index
range access path or complete SQL/native transport. Root keeps all independent
benchmark/serialization/profiling/Garnet edits untouched.

```mermaid
flowchart LR
    Range[SQL BETWEEN operands] --> Lower[Existing typed comparisons]
    Lower --> Truth[Existing eager three valued AND]
    Truth --> Negation[Optional whole predicate NOT]
    Negation --> Gate[Existing budgets and persisted field authority]
    Gate --> Read[Canonical authorized read cut]
    Read --> Public[Real SDK and official MCP results]
```
