# Numeric quality gate design

The repository already mandates file400/type200/function50/nesting3 and coverage
80% line/70% branch (critical/public90% line). Current compiler rules and historical
successful CI contain no configured numeric proof. User requested all checks and
editable source-owned analyzer rules; hiding these missing gates is unacceptable.

Read-only AA research verified the real restore graph: TUnit1.72.10 resolves MTP2.4.1
and Microsoft.Testing.Extensions.CodeCoverage18.11.2; the latter is not centrally
direct-pinned. Official MTP supports Cobertura via its extension. CA1502 measures
cyclomatic complexity only and cannot enforce the four source-size/depth limits.
Coverage collected on the test host does not instrument independent RF3 containers.

Chosen next bounded stage: add four error diagnostics to source-owned Roslyn rules,
with exact token/partial/function/depth metric semantics and real compiler fixtures
first. Preserve original eight imported rules and exact Prostir EditorConfig bytes.
Use a separate future functional coverage job, centrally pin compatible extension,
retain raw Cobertura and verify actual counts; never instrument measured comparisons.
Container-server coverage/export and a matched baseline are explicit unresolved
implementation work, not automatic N/A or invented passing results.

Parallel work: one disjoint numeric analyzer/fixture worker after accepted contract;
lead alone owns central/CI/report policy and broader source refactor integration.
Only ADR041's bounded aggregate DatabaseEngine exception applies; splitting partial
files does not satisfy type size. Token-based LOC avoids comment/brace regex bugs.
Risks: generated and disabled code, multiline literals, partial/nested aggregation,
executable unit boundaries, sibling try/catch branches and generated nesting.

Read-only TASK-MP-010AH-S-DISC maps the36SiteTests source prerequisites. Chosen
direction is a preserving test-source join: finish byte reads before Span-based
comparisons, keep every assertion/oracle/provenance input, use real System.Text.Json
source-generated report metadata at the authentic deserialization call, and repair
bounded process/listener ownership. No site, protocol, report or publication change.
The active website chat is waiting on its separate browser approval and its test
packet is source-complete; inspect live file hashes and stop on any overlapping
write. Two disjoint scopes can proceed: report/assertion source by an economical
worker, lifecycle helpers by lead. Real static-host lifetime regressions precede
lifecycle writes; full GitHub site qualification and browser evidence remain open.

Site source-review refinement (AH-SA/SB): the .NET10 JsonSerializerContext
constructor replaces the supplied options resolver and makes that instance read-
only. Sharing SiteTokens.JsonOptions would break existing real Node request
serialization or fail after another parallel test already used it. Choose a
context-owned copy with identical settings, reached through one actual report-
context factory used by the oracle. First author a regression using the authentic
report and real production Node probe before/after context construction; shared
resolver and options ownership must remain unchanged. Keep DTO defaults and
report inputs; generated missing-property behavior remains a source-review join
prerequisite. Task.WhenAll cleanup may expose one expected pipe error while hiding
an unexpected sibling fault: suppress a secondary capture failure only if every
recorded exception belongs to an explicitly expected category.

Generated-default review found a second real parity defect: .NET10 emits init-only
members as constructor-argument assignments even when JSON omits the property;
absent pseudo-arguments overwrite empty-string, new-object and empty-list initializers
with default(T). The former reflection path creates the object before setting only
present properties. Choose ordinary setters for the seven assembly-internal site
report hydration DTOs, retaining record types, names, property types and initializers.
These are test-only hydrated oracle models, never product contracts or published
schema. This enables actual generated parameterless construction and preserves
omission versus explicit-null semantics without a fallback/dummy factory or new
converter. First compare generated and reflection readers on controlled in-memory
copies of the authentic report with omitted/explicit-null fields. No modified copy
is written, published or counted as measured evidence.

Query test-source prerequisite decision: the real UnitTests dependency cut has
225 errors, including query fixture accessibility/style, culture, cancellation,
constructor-observation and seeded Random findings. Preserve all real caller
assertions and replace random test input selection with an explicit deterministic
schedule whose exercised flows are asserted. Cryptographic or nondeterministic
replacement would lose reproducibility; suppressing CA5394 would hide the enabled
policy. Do not copy an RNG implementation or replace the real database/oracle.

The adapter workload stays200 documents,50 queries,20 numeric values and three
statuses. Coprime index formulas retain distribution, ties, ascending identity
tie-breaks and exact LIMIT30 behavior; invariant rendering removes locale dependence.
The live workload stays100 mutations over12 identities: complete rounds exercise
matching insert/update, leave, nonmatching update, enter, matching delete,
nonmatching insert and re-enter. Assert every required transition was exercised
and compare the real direct-query oracle after every mutation and duplicate replay.
Move the three legacy query test files to canonical QueryExecution ownership as
one reviewed migration; split cohesive fixture/test responsibilities when needed
for type200/unit50/depth3. No production/public/data or framework change is needed.
Independent bounded storage-test discovery and microbenchmark ABI research can
run in parallel; shared docs/CI/config remain with the lead.

Storage-test discovery confirms FlushAsync is not equivalent to Flush(true).
Keep the real synchronous truncate/flush-to-disk/dispose operation, and await a
Task.Run bridge around that whole cohesive physical repair before reopen. This is
test-only blocking I/O; no product hot path or durability barrier moves. Preserve
the deliberate IAtomicStore/IKeyValueView identity proof through an interface-
typed assertion helper instead of narrowing the tested contract. CancelAsync
sets the requested state synchronously, so invoke it at the original visitor
point and observe its completion in finally before further store assertions.
Deterministic frame bytes retain every exact length/base64/tombstone boundary;
distinct cohesive scoped-read/range test classes replace oversized aggregation.

Completed strongest remaining-unit discovery identifies77 preserving findings in
32 files, with37 other findings owned by BCT/native work. Choose canonical
test-source migration and symbol/clock/binding/format repairs, retaining every
original method/argument/assertion; no production or public-contract change.

Two test inputs require explicit decisions. Replace KeyCodec's actual10,000
seeded Random decimals with10,000 fixed SHA256(seed1701,index) inputs encoded
little-endian, using three full32-bit digest words, both alternating signs and
all29 scales by index. Retain six extrema and complete independent round-trip/
numeric ordering oracle; assert count/sign/scale coverage. Do not copy RNG code,
use nondeterministic crypto randomness or claim old20k/100k fuzz coverage.
Remove the BudgetClock double; use actual TimeProvider.System with a five-second
deadline, immediate successful100-byte charge and awaited six-second real timer,
then both existing BudgetExceeded checks. Keep all product timing paths exact.

Source review also finds unobserved admission work on an error path and store
ownership leaks in real fixture constructors/factories. Guarantee task completion
observation in cleanup, and transfer fixture ownership only after complete setup.
Real caller-supplied fresh fixture directories support deterministic failure/
reopen assertions; this is actual filesystem configuration, not a fake service,
fault hook or product seam. Shared TestDatabase belongs only to the lead.
Independent workers own explicit disjoint files; concurrency slots, not invented
dependencies, determine queued order. No local runtime execution is authorized.
## CQ016 fixture review refinement

The strongest source review found that a supplied existing TestDatabase directory
could be deleted after an open/lock failure, and that a throwing store Dispose
could skip directory cleanup. The owned-directory option means a fresh path.
Reject an existing directory before acquiring or modifying anything; prove that
real existing files and an active ZoneTree owner survive rejection. Use finally
for owned-root cleanup after disposal is attempted. No synthetic disposal fault
or product/storage contract change is needed; environmental exceptions retain
complete source-review evidence alongside the real invalid-limit/reopen tests.
# Ordered byte-array assertion integration

Pinned TUnit equality compares arrays by reference. Read-only source review found
twelve existing content-oracle sites: six in owned FrameBudgetTests,
PreparedTransactionTests and ArtifactTests, and six in independently edited native
ClusterRouting tests. Replace only the six owned assertions with ordered content
equivalence, preserving exact bytes/null failure, actual store/reopen/transfer and
all other assertions. Blindly replacing every equality would weaken intentional
identity checks. Native sites remain a protected integration dependency until their
owner has completed; no concurrent writes to those six assertions. Root owns the
already corrected new gate/shared-fixture assertions. No production/dependency or
measurement changes; source semantics is not a runtime failing-test result.

## Reviewed-source formatter and prerequisite join

The latest UnitTests dependency build stops before UnitTests compilation: the
new enclosing KeyLoad.BlobMetadata shadows the existing provider BlobMetadata in
ArtifactTransfer. Preserve its original ManagedCode.Storage return type with an
explicit alias; do not map metadata, change the dependency or alter transfer work.
The existing real transfer regression and ordinary compiler binding are its proof.

The reviewed-unit formatter reports two generic catches in the private admission
task owner, two unnecessary lambdas and whitespace in five QueryExecution files.
Resolve the exact SDK CA1031 semantics before choosing a preserving task-failure
boundary. Every failure and real worker await must survive; suppression, fake
exceptions, task-status polling or dropped cleanup are not options. Two method
groups and whitespace formatting preserve existing input/order/assertion semantics.
The broader formatter scope also included native BlobStorage work still owned by
another chat; those observations are neither this stage's write scope nor proof
that the reviewed source compiled.

Exact SDK10.0.401 primary-source review confirms CA1031 accepts an actual throw
of a wrapping AggregateException in a general catch. Choose one private wrapper
around callback execution, collect only that specific wrapper's direct inner
exceptions, and observe each raw worker through the same boundary. Do not Flatten:
an original empty AggregateException is still a real failure object. Keep exact
expected cancellation handling in the actual worker await, original post-release
bounds, cleanup ordering and unconditional raw task observation.

The five-file whitespace diff preserves tokens but expands the live mutation
method to54 token-bearing lines and the adapter workload method to52. Its live
test type remains197; adding a same-type helper would exceed200. Extract only
the final live coverage block and the post-commit/pre-query adapter document
coverage block into one cohesive canonical QueryWorkloadCoverageAssertions helper.
Pass the same arrays/count constants and await at their original points. Preserve
each loop/assertion and failure ordering; never compact lines to evade the limit.
