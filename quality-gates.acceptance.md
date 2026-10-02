# Numeric quality gates acceptance

REQ-CQ-006, AC-CQ-008/009; ADR-033 extension. Compiler/IDE maintainer and CI publisher
are actors; central analyzer attachment and canonical workflow are entry points.
No database API/data/security/replica/measurement change. No tools/skills installed.

AC-CQ-008 accepts the following source-owned error diagnostics, enabled by default:
KLD0030 file code LOC>400, KLD0031 aggregate type code LOC>200, KLD0032 executable
unit code LOC>50, KLD0033 control-flow nesting>3. Boundaries at the limits pass;
one over fails at useful exact source locations. All original rule IDs/severities,
warnings-as-errors, official generated-code exclusion and exact EditorConfig remain.

Metric contract:
- Code LOC counts distinct physical lines containing a nonmissing/nonzero Roslyn
  token in the measured span. Blank/comment-only/XML-only lines do not count;
  multiline token contents, including raw/interpolated literals, do count. Nonblank
  preprocessor/disabled-text lines also count so disabled source cannot hide size.
- File metric covers every nongenerated C# tree. Type metric sums nongenerated
  partial declarations by INamedTypeSymbol; nested type declarations count within
  the containing type and separately as their own type. Same-line tokens count once
  per declaration; no partial-file loophole. Header/attributes/body/braces count.
- Executable units are methods, constructors (including static), destructors,
  operators/conversions, accessors, expression-bodied properties/indexers, local
  functions and lambdas/anonymous methods;
  full header/body/expression spans count. Inner unit source contributes to the
  enclosing unit as well, and inner units are separately checked.
- Nesting counts executable if/loops/switch statement and expression/try/using/lock/
  fixed constructs and conditional expressions on a nested execution path. A catch
  or finally body is a sibling branch of its try body (one try-level, not an extra
  catch-level). Else-if remains the nested if shown by source; braces alone add no
  level. Nested function/lambda starts a fresh executable depth for its own body.
  Using declarations have lexical lifetime but add no nested body/depth.
- Analyze concurrently and cancellably, using Roslyn syntax/symbols, no regex brace
  parsing or severity knobs. Generated-code exclusion is the official analyzer
  mechanism, not new handwritten product GeneratedCode tags to avoid the gate.
- Only exact KeyLoad.Core.DatabaseEngine in assembly KeyLoad.Core receives the aggregate
  type exception documented ADR041, before UTC2026-11-01. File/function/depth remain
  mandatory; UTC2026-11-01 expires it. A pure date/identity predicate may be tested
  with date values; do not add fake clocks or configurable user bypasses. Keep the
  aggregate type migration open, not a claim it passes the ordinary threshold.

| AC | Positive/negative/edge/error flows | Automated proof |
|---|---|---|
| 008 | File399/400/401, type199/200/201, function49/50/51, depth2/3/4; compiler-valid real C# inputs; exact ID/error/path/start-line/start-column for at least one diagnostic from each rule; blank/XML/comments; ordinary and raw/interpolated multiline literal spans; directives and disabled text inside file/type/unit scopes; partial/nested types; expression-bodied property/indexer and distinct accessor/local/lambda units; if/else-if, every loop form, switch statement/expression, try/catch/finally sibling paths, using statement/declaration, lock, fixed and conditional expression; generated source and DatabaseEngine exception identity/expiry | First-authored SDK Roslyn TUnit fixtures; compilation errors asserted empty before analysis; source-location assertions cover the reported token/entity boundary; source self-inventory includes both excluded-infrastructure projects; exact-SHA analyzer-rules job and ordinary complete consumer build retain SARIF |
| 009 | Collector pin/registration, raw reports/counts, missing/malformed/zero and absent required module failure, 80/70/90 boundaries, real matched baseline, RF3 server collection/export | Future dedicated report verifier fixtures + separate real functional coverage job; pending accepted implementation details |

AC-CQ-009 remains mandatory unfinished work: centrally pin the actual compatible
MTP extension; collect coverage separately from measurements; count per-file source
lines and branch conditions from Cobertura, not rounded aggregate percentages;
fail closed on missing reports/required modules/denominators. Test-host data alone
does not qualify server containers. No matched numeric base exists yet; do not claim
the no-decrease rule or mark server code covered. The report verifier/container
collection design requires its own precise accepted stage before implementation.

Source analysis is static proof; new rule fixtures execute only in GitHub Actions.
Maintainability and coverage claims require actual gates; no ignored finding is a
pass. Migration: compile/IDE diagnostics, no persistence. Rollback must preserve
mandatory policy and needs owner direction before weakening an enabled requirement.

## Accepted preserving consumer joins after storage

The actual enabled solution cut `strict-solution-private-storage-join.log` exposes
54errors/0warnings: SiteTests36, CLI13, Query3, Security1 and a comparison session
interface omission1. Failed dependencies leave later graphs uncompiled;54 is not
an exhaustive all-project finding count. Existing source contracts remain exact.

AC-CQ-010: CLI Program is only an aggregate typed runner invocation; the existing
eight command names, arity dispatch, stdout/help/JSON bytes, stderr problem JSON,
exit0/1/2, path normalization and offline backup/restore/compact/archives/status
calls remain exact. Credentials retain environment-first/profile-second precedence;
no role/endpoint/profile/trust change or secret output. Feature behavior moves to
ClientApi and BackupRestore, no duplicate SDK/storage implementation. Named const
keys and resource-backed original English messages satisfy existing analyzers;
only stderr switches to awaited WriteLineAsync with unchanged serialized contents.
Pass: all CLI file/type/unit/depth400/200/50/3 and original behavior source parity;
fail: new fallback, command, default, swallowed exception, changed message/exit,
credential exposure or skipped real checks. Existing real backup/artifact/.NET
flows remain CI proof of invoked components. Explicit manual source-parity evidence
covers the unchanged thin command dispatch/text/exit migration; it adds no behavior
requiring a fake CLI test. Full integrated compile and formatter are static proof,
not runtime command qualification. Exact-SHA real CLI process smoke coverage is
still required before claiming that executable itself qualified.

AC-CQ-011: QueryEngine's public SQL/AST/live signatures, requests and cursor JSON,
authorization/admission/cancellation/budget order and projection results remain
exact; a cohesive private live-query owner borrows the existing database and same
single-cut view. No new view, cache, gate, re-admission or parsing. SearchTerms retains
Unicode normalization, token boundaries/counts/lengths and exact charge/check/error
order. Every real type/unit/depth400/200/50/3 passes. Existing real LiveQueryTests,
QueryAdapterTests, scoped QueryExecution/ResourceExecution and search/error/budget
cases are the CI proof; complete source comparison covers unchanged private moves.
No new valid-input exception, public compatibility shim or weakening is accepted. Security's
redaction guard refactor is lead-owned: exact object/array wildcard/index removal,
missing/null behavior and recursion order remain; existing real field-redaction/
permission/negative flows qualify through CI and full source parity is reviewed.

AC-CQ-012: the externally visible QueryEngine constructor rejects a null database
with ArgumentNullException whose ParamName is database, before constructing its
private collaborators. The previous unvalidated invalid input is not a supported
compatibility path. Every non-null database and all operation contracts retain
AC-CQ-011 semantics. A first-authored real TUnit QueryConstructionTests case
asserts the exact exception and parameter, with no fake database; existing real
query suites cover valid construction. Source build is not test execution.

AC-CQ-013: SiteTests compiles under the same mandatory policy without removing
or weakening any existing test/assertion, changing authentic report inputs, Node
protocol, site assets or publication. Await byte reads into locals before byte-
exact comparison; nullable exception results retain positive message assertions;
test classes are internal, numeric conversions use invariant culture and caller
cancellation is fully awaited. A real source-generated SiteReport JSON context is
used by the existing report read with an independently owned copy of the original
web JsonSerializerOptions settings. Context construction must leave the shared
options resolver and read-only state unchanged and work after real Node request
serialization has already used those options. Generated DTO constructor calls
and default preservation are reviewed, never dummy constructions. The seven
assembly-internal site-report hydration records may use ordinary setters in place
of init-only accessors to preserve the former reflection reader's omitted-property
defaults; record/property names, types, initializers and serialized schema stay
exact. Controlled in-memory copies of authentic input must prove generated versus
reflection parity for absent root/nested defaults and explicit null. They are
negative test inputs only and cannot be published as benchmark evidence.
Only known process/pipe/capture cleanup failures may be secondary to the original
rethrown failure; unexpected errors propagate, including a sibling fault hidden
by Task.WhenAll's first observed exception. Every acquired HttpListener is
closed unless transferred to the live host, and disposal closes all owned resources
even after cancellation/serve failure. Sequential repeated disposal is harmless.
Pass: ordinary enabled SiteTests build, unchanged byte/provenance/oracle assertions,
first-authored real static-host serve/dispose/closed-listener and invalid-root
regressions and a first-authored authentic-report/real-Node before/after context
options regression, then exact-SHA complete GitHub site suite. Private failure-transfer
paths retain full source-review evidence; no fake server/handler or local tests.

| Criterion | Required automated proof | Level and command | Manual evidence |
|---|---|---|---|
| CQ013 listener ownership | SiteProcessOwnershipTests real serve/close/repeated disposal and invalid root | GitHub SiteTests MTP command in CI; no local execution | every untransferred listener and response finally path |
| CQ013 JSON ownership/defaults | SiteReportSerializationTests authentic report plus real Node before/after context, same shared resolver and option state; omission/explicit-null copies compared with reflection | same complete GitHub SiteTests suite | actual generated parameterless constructors, original defaults and unchanged JSON schema |
| CQ013 preserving joins | Existing SiteBuildTests, SiteEvidenceValidatorTests and SiteMeasurementOracleTests, all assertions retained | same complete GitHub SiteTests suite | exact source/assets/provenance diff, bounded cleanup and every aggregate failure |

## Preserving deterministic query test source

AC-CQ-014 / REQ-CQ-007: all existing SQL/JSON/C# equivalence, access paths,
authorization/redaction, cursor/revocation/cut, null/missing/IN, unsupported CLR
getter non-execution, parser limits/error precedence, live replay/tail/oracle,
history-loss and cancellation/admission-release assertions remain. Tests use the
real TestDatabase/ZoneTree with persisted policy; no substitute dependency exists.
Compilation/style repairs cannot change an expected result, exception or boundary.
Constructor rejection lambdas explicitly observe discarded construction; do not
replace them with Parse calls that change the exercised boundary. CancelAsync is
awaited before operations use the cancelled token. Unsupported StartsWith remains
an unsupported method call with an explicit char/ordinal overload.

Adapter deterministic input has exactly200 documents,50 queries,20 values0..19,
all three original statuses and LIMIT30. Named seeded coprime formulas or explicit
tables replace Random; invariant numeric/identity formatting preserves wire text.
Assert each status contains every numeric value (including ties), all20 thresholds
and three query statuses are exercised, and a real result reaches the30-row cap.
Every query still compares complete serialized rows and access paths across all
three actual adapters; a trivially empty workload fails the independent assertions.

Live input has exactly100 mutations over12 identities. Each12-step round visits
every identity once. Ordered rounds are matching insert, matching update, leave,
nonmatching update, enter, matching delete, nonmatching insert and re-enter; final
four steps are matching updates. Track/assert actual transitions, nonmatching
cursor advancement and full replay bytes; retain revision/tombstone ownership,
duplicate replay application, drain behavior and the direct-query oracle after
every commit. No copied RNG, crypto randomness or random security exception.

Migrate only QueryAdapterTests, LiveQueryTests and SecurityAndQueryTests from
legacy root ownership to Features/QueryExecution, with original test methods and
assertions retained in cohesive internal classes. Remove the old files in the
same source join, leave no duplicate or partial-type size loophole, and enforce
400/200/50/3. Existing QueryConstructionTests, SqlParserContractTests and
QueryResourceTests receive only preserving analyzer repairs. Shared TestDatabase,
production source, native/MCP tests, projects/packages/workflows and policy are
outside this worker's scope. Rollback is the complete test-source unit with all
original assertion coverage retained; there is no persisted/public migration.

| Criterion | Automated proof / level | Required command | Review exception |
|---|---|---|---|
| CQ014 adapter | Existing actual three-adapter cases plus workload extent/tie/threshold/cap assertions | Exact-SHA GitHub UnitTests MTP command after Release build | before/after method/assertion and invariant-text review |
| CQ014 live | Existing real mutation/oracle/replay cases plus actual transition/cursor assertions | Same complete GitHub UnitTests suite | deterministic12-identity/100-step schedule and revision mapping review |
| CQ014 negative/budget | All existing permission/getter/null/cursor/parser/cancellation rejection cases | Same UnitTests plus complete required recovery/RF3 CI | constructor/cancellation/source parity and actual generated registration |

Pass requires complete enabled graph build, formatter, source/diff/registration
review and real exact-SHA CI, not merely source-authored fixtures. The historical
main baseline does not qualify these edits; current prerequisite compilation and
coverage blockers remain tracked without weakened acceptance.

## Preserving storage test source

AC-CQ-015 / REQ-CQ-007 / REQ-STORAGE-010: retain every existing real store test
and assertion in the seven-file discovery scope: CheckpointTests, FrameBudgetTests,
PartitionHostRecoveryFixture/Tests, ScopedRangeBudgetTests, ScopedRangeTests and
StoreLifetimeTests. No fake storage, callback or process, production/format/API
change, exception substitution, test skip or diagnostic suppression. Internal test
classes and the three standard constructors on private failure-marker exceptions
retain original parameterless throw sites and distinct expected exception types.

Corrupt-journal repair retains exact order: write the complete52-zero corrupt
header, reject its construction, prove exclusive owner lock is released, create/
truncate commands.wal, Flush(true), dispose, then reopen and commit. Await async
byte-file APIs where equivalent, but never replace the physical barrier with
FlushAsync. A named cohesive synchronous truncate-and-flush-to-disk operation may
run through an awaited Task.Run bridge; it completes its disposal before reopen
and has no cancellation shortcut. Preserve the real IAtomicStore-typed dispatch
and IKeyValueView identity assertion through an interface-typed assertion helper,
without retyping the tested boundary or adding a store substitute.

Pre-cancellation is fully awaited before reads. Inside synchronous Commit/visitor,
invoke CancelAsync at the exact prior Cancel statement after the visit count
advances; requested state must stop the next record and abort persistence. Always
observe the captured completion in finally before later store operations, including
an assertion failure. Retain one-visit/error/unchanged-state/store-usability proof.

Frame input retains12 trials and lengths trial*17 and trial*19+5, FB FF/FF FB keys,
all final mutation ordering/tombstones, byte-exact serialized frame/checksum/read
and exact/one-byte-short oversize assertions. Named deterministic byte patterns
replace Random, preserving reproducibility and base64 edge cases; no RNG copy,
crypto/nondeterministic selector or reduced trial count. All checkpoint corruption,
truncation and publication assertions keep their original positions and values.

Split scoped-read/range responsibilities into distinct non-partial canonical
StorageRecovery test types only when required for400/200/50/3. Every original
method/assertion is mapped once, including overlay order, raw byte/tombstone caps,
signing-key ownership, cancellation, visitor failure and later store usability.
CheckpointTests remains its recorded ADR032 legacy file for this bounded repair;
new feature-owned helpers live under Features/StorageRecovery. ReadDiagnostics
and other files outside the seven-file scope require a separate accepted join.

| Criterion | Automated proof / level | Required command | Review supplement |
|---|---|---|---|
| CQ015 frame/checkpoint | All original real FrameBudget/Checkpoint scenarios and exact byte/error/state assertions | Exact-SHA GitHub UnitTests MTP command after Release build | first source/assertion map and deterministic byte/length review |
| CQ015 lifetime/interface | Existing corrupt-header/lock/reopen and IAtomicStore view identity cases | Same complete UnitTests suite | actual .NET10 Flush(true) versus async implementation; awaited barrier/disposal ordering |
| CQ015 scoped/cancel | All original read/range/overlay/budget/failure/cancellation scenarios and actual partition-host recovery cases | Same UnitTests plus full process recovery and Docker RF3 CI | one-to-one method registration, captured cancellation finally and numeric limits |

Rollback is the coherent preserving test-source unit. No persisted migration or
power-loss, memory measurement or coverage result follows from these cases. Pass
requires reviewed source, full enabled build/formatter/static and actual exact-SHA
GitHub execution; historical baseline and source-authored changes are insufficient.

## Remaining real unit source and fixture ownership

AC-CQ-016 / REQ-CQ-007 / AC-TEST-001/006: preserve every132 original method in
the32-file strongest-reviewed scope and the single extra StreamReadResourceTests
caller;132 is a source inventory, not an executed count. Native ClientApi/
ClusterRouting and BCT sources remain separately owned. Every existing argument,
byte/error/permission/limit/lease/healthy-follow-up assertion stays meaningful.
Before/after maps and complete source hashes are required, with no fake, skip,
suppression, concrete retyping of a tested interface or production/API change.

Migrate all19 original methods from root KeyCodecTests/GraphAndSearchTests/
SignedEnvelopeTests/ReadExecutionTests exactly once to canonical StorageRecovery,
GraphTraversal, TimeSeries, Search, Authorization and ResourceExecution owners.
New cohesive helpers/test names are listed in the graph. Remove all replaced
root files in the same source join; no duplicated tests or partial-size loophole.

The decimal corpus intentionally changes: use fixed seed1701 plus each index
0..9999 encoded as two Int32 little-endian words, SHA256.HashData, and the first
three Int32 little-endian digest words as the decimal96-bit coefficient. Sign is
index%2==0, scale index%29. Retain all six original extrema, exactly10006 total,
every round trip and whole numeric-sort oracle. Assert determinism,10k workload,
both signs and every scale. Named constants replace machine-key strings/seed/
shape values. No Random implementation or nondeterministic replacement remains.

The real deadline case uses TimeProvider.System, QueryDeadlineSeconds5, immediate
ChargeBytes100 before any await, then Task.Delay6seconds with the same real provider
and current test cancellation token. Preserve both following byte-charge and
text-token BudgetExceeded assertions; remove BudgetClock entirely. This is
elapsed-deadline proof, not cancellation-only or zero-deadline substitution.

Replace DateTimeOffset.UtcNow with TimeProvider.System.GetUtcNow at the exact old
call points, without freezing/advancing host time or changing existing business-
time scheduling. Add real storage extension imports and exact span/immutable/
memory bindings; use ordinal matching where current ASCII test characters prove
equivalence. Await cancellation at its existing point; retain physical barriers.
Split only cohesive helper responsibilities required by400/200/50/3.

Ownership corrections: admission cancellation cleanup must cancel as necessary,
release the real held gate and observe the actual admitted task on every error
path before disposing the database. Preserve its exact cancellation assertion
and subsequent healthy-search proof. Register every actual worker as soon as it
starts, but start both admitted-search completion observers only after cancellation
and gate-release attempts. Preserve the original ten-second post-release success
timeout and the exact OperationCanceledException assertion with that same timeout.
After either bounded observer fails, always await its underlying worker to actual
completion; only the exact expected cancellation may be handled during final
observation. No indefinitely delayed worker can turn a timeout into a passing test.
The lead additionally owns the existing RealZoneTreeReadGateHold and a new
ResourceExecution/ReadGateLifetimeTests: release is signalled before the bounded
wait and the actual holder is observed in finally, including disposal after a
failed initial entry wait. Real immediate-disposal and entered-gate/repeated-disposal
cases prove subsequent store usability; environmental holder-timeout/fault paths
receive an explicit full-source lifetime audit because synthetic callbacks or
exception injectors are forbidden. This is the exact accepted ownership extension;
workers may not edit the shared gate helper. Fixture constructor/factory setup transfers
real store/TestDatabase ownership only on success; failure disposes before deleting
its owned directory. Root alone adds a caller-owned fresh-directory option and
failure-safe acquisition to shared TestDatabase, with real invalid-limits/reopen
regressions. An already existing supplied directory is rejected with
ArgumentException before any acquisition or mutation; real files and an active
ZoneTree owner remain usable. Owned-root cleanup runs in finally after store
disposal is attempted, including an environmental disposal exception. Stream
fixture may likewise accept an actual fresh directory; an
invalid real event count proves setup failure cleanup. Its supplied existing
directory is likewise rejected before acquisition/cleanup ownership, with real
empty-directory and active-owner/file preservation proof. Topic factory may receive
that owned directory and exercise actual too-small accepted batch limits to
prove setup failure cleanup. No synthetic exception injector or fake dependency.
Every test-owned reopened directory is cleaned in an unconditional outer finally,
after reopened owner disposal, including assertion/reopen errors. Throws lambdas
scope an unexpectedly successful returned IDisposable. Topic rejection is asserted
as ResourceExhausted at actual post-acquisition ConfigureResource setup.
Environment-only cleanup failures and failed coordination branches require full
source lifetime review as explicit supplemental evidence, not a claimed fault run.

| CQ016 obligation | Planned automated proof / command | Review supplement |
|---|---|---|
| canonical132-method/19-root preservation, binding/bytes/cursors/authorization | Existing complete real UnitTests TUnit/MTP suite in exact-SHA GitHub after enabled Release build | Full original→final method/argument/assertion/source map and generated registration |
| deterministic10006-decimal corpus and real elapsed deadline | Migrated codec/deadline methods with added workload/coverage assertions, same GitHub command | Independent fixed-seed/index byte order and all scales/signs; no doubles |
| task/fixture failure ownership | Actual admitted cancellation + new real invalid-limits/event-count/batch setup failure, same-path reopen and existing-directory/active-owner preservation tests in GitHub | Complete acquisition/cleanup/task observation audit including environmental error paths |
| strict policy and whole slice/regression join | Full enabled build, canonical formatter/governance/import/numeric plus complete UnitTests/recovery/RF3 SDK/MCP GitHub gates | Source builds alone are not test/resource/coverage proof |

ADR033/032 and existing feature contracts suffice for these private test-source
and fixture-lifetime changes; no product boundary/data/API/trust migration occurs.
Rollback is the complete preserving test/fixture unit, retaining all original
scenario coverage while keeping mandatory no-fakes/static policy. Completion
requires all mapped actual gates, not this acceptance or a worker source claim.

CQ016 preserving formatter follow-up: replace only the diagnosed UTF8.GetBytes
selector and parameterless ChargeTextToken lambda by their equivalent method
groups. Keep the KeyCodec.Encode params-component selector unchanged unless the
actual formatter diagnoses it; its string-to-object component binding must remain
exact. CQ014's five diagnosed QueryExecution files may receive whitespace-only
formatting. Full token/assertion preservation and enabled numeric compilation
remain required; formatting is not permission to reduce a test or add a size
exception.

CQ014 numeric follow-up after whitespace formatting: extract the final live
identityVisits/transition-count coverage block and the adapter documentCoverage
nested assertion loop, immediately after the same db.Commit and before query
setup, to NEW QueryExecution/QueryWorkloadCoverageAssertions.cs. Use the same
arrays and existing StatusCount/NumericValueCount constants without copies or
changed formulas. Keep every assertion/loop/failure order, original six live and
one adapter Test methods, all other query peers and the200-document/50-query/
100-mutation workloads. A same-type live helper would exceed type200, so this
cohesive independent helper is the accepted private test ownership. Ordinary
enabled numeric build/formatter, full source maps and exact-SHA GitHub complete
query/unit/regression gates prove it; source-authored extraction alone does not.

Artifact prerequisite under AC-CQ-001/007 and AC-CQ-018: the existing
ArtifactTransfer.CopyToFileStorageAsync return type remains exactly
ManagedCode.Communication.Result<ManagedCode.Storage.Core.Models.BlobMetadata>.
An explicit provider type alias prevents enclosing KeyLoad.BlobMetadata lookup.
No upload call, URI/path, archive bytes, dependency/package or public CLR contract
changes. Static compiler binding and the existing real ManagedCode transfer test
in exact-SHA GitHub are required; no artificial mirrored behavior test is added
for this source name-resolution repair. Existing ADR033/008/046 suffice.

CQ016 / AC-TEST-006 B3 failure boundary: a private async callback wrapper catches
Exception only to throw a new AggregateException containing that same error as
its single direct inner exception. CaptureFailureAsync catches AggregateException
from this wrapper and appends its direct InnerExceptions, never Flatten or discard
an empty original aggregate. Operation, cancel, release and original bounded
post-release observer are still attempted in order. Finally observes every actual
immediately registered worker, with only the exact expected OperationCanceledException
handled inside its actual await. Unexpected errors, cancellation subclasses,
timeouts, callback assertions and cleanup failures remain in the final aggregate;
no failed path becomes success or allows a task to outlive store disposal. No
extra scheduling, Task.WhenAny/status substitution, fake injection, suppressed
diagnostic or incomplete list of allowed exception types.

Verification: existing genuine admitted-success/saturation and in-flight-cancel
TUnit flows execute in exact-SHA GitHub UnitTests after ordinary enabled build.
Environmental callback/cleanup-error paths receive the already required complete
source-lifetime audit; no artificial mirrored or fake fault test is added. The
actual SDK10.0.401 CA1031 throw-operation rule and final enabled compiler/formatter
are static evidence only. Private helper architecture/contracts remain under
ADR033/032 with no product/public/persistence change.

## Ordered content assertions under the pinned framework

AC-CQ-018 / REQ-CQ-007 / REQ-STORAGE-009/010 / REQ-BACKUP-001: exactly six owned
byte-content assertions use ordered equivalence under pinned TUnit1.72.10.
FrameBudgetTests retains both exact final-value readbacks for all twelve existing
trials before and after rejected oversize commit. PreparedTransactionTests retains
both original [0x40,0x41,0x42] committed/reopened values after caller mutation and
the original [0x31,0x32] value after rejected replacement. ArtifactTests retains
the complete original archive versus ManagedCode file-storage copy byte comparison.
Pass requires same lengths and every byte in the same order; null, changed, missing
or reordered bytes fail. No reference-identity oracle is converted. Exact inputs,
WAL/barriers/positions/error codes, replay/reopen, test methods/arguments, workload
and allocation windows remain unchanged. Materialize the two already-awaited
artifact file reads into locals before the ordered assertion; no new file read,
public/production/dependency/package change or partial hash substitute.

| Obligation | Planned automated proof / command | Review supplement |
|---|---|---|
| frame exact bytes through successful/rejected commit | Existing twelve-trial FrameBudget method; exact-SHA GitHub complete UnitTests MTP after Release build | Six-site original/final assertion map, pinned EqualityComparer semantics, unchanged exact byte arrays |
| prepared replacement/reset/caller ownership/reopen/rejection | All three existing PreparedTransactionTests; same complete GitHub UnitTests command plus required recovery/RF3 regressions | Actual owned copies versus independent expected buffers; no identity-test weakening |
| archive/storage transfer bytes | Existing real Cartograph/ManagedCode copy test; same GitHub UnitTests command | Complete awaited byte arrays and ordered equivalence, original three Artifact methods retained |
| protected native request/signature/retry bytes | Existing six ClusterRouting sites require a separately safe native-owner integration join and exact-SHA GitHub Unit/RF3 gates | This stage has no authority to overwrite actively edited native files |

ADR: N/A for new decisions; ADR033/035/046/008 already govern these private test
oracles and unchanged actual storage/backup contracts. Rollback keeps meaningful
ordered byte proof under the active framework; returning to reference equality is
not a passing regression. No tests or resource improvement are inferred from this
read-only inventory or source correction.
