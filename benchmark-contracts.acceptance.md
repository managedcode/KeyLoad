# Benchmark immutable contracts acceptance

REQ-BC-020; ADR-044. Actors: harness/library caller, engine adapter, report reader,
configuration producer and CI maintainer. Entry points: corpus, public records,
ComparisonOptions.Read, serializer/report writer and real engine sessions. No
database credential, public SDK, placement, durability or persisted data changes.

In scope: diagnosed harness collections, cached oracle returns, CLR topology and
exception naming, all source callers and source moves into BenchmarkComparisons.
Out of scope: PostgreSQL DDL redesign, nine-engine registration, site measurements,
timing/statistics changes, SIMD oracles, packages and unapproved compatibility shims.

- AC-BCT-001: Vector, optional Neighbors/Vertices, Observations, Samples,
  report Targets/Cases and corpus Documents/Edges use typed ImmutableArray.
  ExactNeighbors and Reachable return immutable cached arrays, reused on cache hits.
  Fail: public mutable array aliases, defensive-copy getters, mutable cache truth,
  changed order, identifier, float bit, event identity or corpus SHA.
- AC-BCT-002: required DTO arrays serialize as initialized arrays including empty [];
  optional arrays distinguish absent null from present empty []. Required missing,
  null and default arrays and present nullable default arrays reject explicitly.
  This malformed-input rejection is intentional hardening. Existing valid schema3
  report JSON bytes/field order/indentation/enum text and CSV attempt order remain
  exact; older valid reports with present array fields still deserialize. Do not
  promise old-schema byte roundtrip when current optional metadata adds fields.
  Constructor-owned getter-only corpus properties are generated from validated
  options, not DTO deserialization inputs; required setter metadata does not apply.
- AC-BCT-003: CLR Standalone retains numeric 0 and Replicated retains 1. JSON and
  configuration retain Single/Replicated and existing default/range behavior.
  Configuration conversion preserves existing case/whitespace, numeric and comma
  EnumConverter behavior; malformed, empty and undefined values preserve binder
  and validation failure boundaries. The new CLR spelling is accepted additively.
  No Single enum alias or duplicate legacy API. Existing producers continue to
  write Single rather than accidentally using changed enum ToString output.
  The source-owned TypeConverter is a documented public configuration extension
  referenced by the enum attribute and exercised through external TypeDescriptor
  callers. Preserve the base object parameter nullability contract and actual
  conversion/failure semantics; do not add an unused instantiation or broad IVT
  solely to evade CA1812 reflection reachability analysis.
- AC-BCT-004: ComparisonFailureException replaces the old type everywhere, with
  standard no-argument, message and message/inner constructors. Existing coded
  messages, exception-safe classification and original inner instance are retained;
  report-safe output never includes inner secrets. No legacy exception shim.
- AC-BCT-005: exclusively owned final buffers attach once; caller-owned arrays
  freeze once before becoming immutable truth. Samples attach only after measured
  execution and validation finish. KeyLoad passes immutable vectors directly to
  existing immutable SDK contracts; OpenSearch/Qdrant and PostgreSQL serialize
  identical numeric vectors without per-use ToArray copies. Scalar cosine, native
  receipts, successful setup/request order and every operation sample are unchanged.
- AC-BCT-006: exact disjoint packets join under the lead, normal dependency build,
  formatter/static gates and exact delivered-SHA canonical CI. No local tests,
  doubles, suppression, weakened analyzer, missed suite or source-only performance
  claim. Keep ADR Accepted until every required implementation and evidence exists.

| Criterion | Positive, negative and edge assertions | Automated verification |
|---|---|---|
| 001 | Independent corpus identity/float/hash fixture; cache reuse; no array public properties; external source-array mutation cannot change frozen data | First-authored TUnit collection/corpus contracts; existing StreamCorpusTests; real engine suites |
| 002 | Fixed schema3 fixture, Unicode/error text, exact valid JSON bytes, null/empty/default/missing cases, real file/CSV cancellation and attempt retention | First-authored real JsonSerializer contracts; ReportFileTests; canonical CI |
| 003 | Single and Replicated JSON/config; case/trim/numeric/comma/invalid/undefined/default paths through real ConfigurationManager/binder | First-authored ComparisonTopologyTests; actual host startup tests; CI |
| 004 | All standard constructors/message/inner identity, null-message behavior and safe output without secret text | First-authored ComparisonExceptionTests and report error cases; CI |
| 005 | Corpus and numeric vector shape unchanged; cached reuse; every failed/successful attempt retained after validation | Corpus/report contracts plus all real Docker/Aspire engine comparisons; CI |
| 006 | No skipped suite; correct analyzer/format/SARIF and immutable artifact SHA | Build/static review then exact-SHA GitHub unit/recovery/RF3/MCP/comparison/analyzer gates |

Testing methodology: real serializer/configuration/compiler and file inputs are
contracts, not substituted engines. Author fixtures before production edits. Existing
fake-target/handler tests are tracked MP-032 debt and cannot qualify this stage.
CI-only execution and the failing graph prevent a new green/red runtime claim now.
The recorded successful base-SHA full suite is historical baseline only; no numeric
coverage baseline exists. Static source-token review verifies moves and allocation
ownership, but does not prove throughput, RSS or numeric coverage. Required line,
branch and complexity policy remains pending its real configured gates.

Explicit evidence gaps: vector-bit/event fixtures derive from frozen source formulas,
not measured CI. No authoritative pre-stage corpus SHA golden is retained; do not
invent one. The unchanged generator/hash statements require exact source review and
real engine/corpus CI before this criterion closes. Standard exception constructors
are automated, while inner-secret safe report classification receives preserving
source review plus required real engine setup/error CI; no fake target, reflection
route or new public helper is introduced solely to simulate that flow.

Migration/rollback: build-time harness CLR break is contained in this solution,
all callers move together; no published package consumer is promised compatibility.
Valid report/wire/configuration values remain stable. Revert this scoped contract,
consumer and test unit together if rollback is needed; do not add fallback APIs.
