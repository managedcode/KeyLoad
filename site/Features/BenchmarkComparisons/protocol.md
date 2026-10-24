# Website session evidence protocol

This is the source contract for the current intake, not proof of successful GitHub qualification. The only producer is the Benchmarks `comparison-aggregate` job with the `comparison-session-suite` artifact; provider originals are retained in `comparison-session-provider-evidence`. The schema/profile is `session`. Historical results are never converted into current measurements.

## Canonical composition and measurement bounds

The inventory contains exactly 32 database/topology jobs: 16 databases × 1/3 nodes, with a separately named matrix for each database. Each runner owns only one database and its real topology. Separate real TUnit tests share one live database session. The initial load of 1,000,000 records is measured separately; there is no warmup, reset or five-repetition scheme. Two-node configurations are not part of the active inventory.

The [additional database contract](../../../docs/Features/BenchmarkComparisons/AdditionalDatabases.md) records the five added native targets, one-node deployment scope and open three-node work. Their unavailable topology rows are not replication qualification.

The seven current case IDs are `load-million`, `index`, `sequential-read`, `random-read`, `search`, `mixed` and `verify-final`. Operation lanes execute 100,000 operations each; mixed runs create/delete/update/read/search with 16 clients distributed 4/3/3/3/3. The first load makes one million real create attempts. Original samples retain the operation ordinal, worker, time bounds, success and a safe error. Intake recomputes counts and latency percentiles from the originals and verifies the participation of the configured workers and the time overlap of the mixed lanes. Participation of every worker ID alone does not prove that all clients were active simultaneously.

These seven document-session tests do not prove completion of every product model family, vector accuracy, queue/event contracts or general performance qualification. Absence of a capability has an explicit unavailable result without numeric metrics; adapter/prerequisite gaps are failures. When all seven document cases are genuinely unavailable for Qdrant/RabbitMQ/KurrentDB, the envelope carries `SessionCapabilitiesUnavailable` and a null observed profile but retains the actual image and resource originals. This is not topology unavailability. A failure before profile verification may also have a null profile, but only when no case was measured and actual exit/qualification failure proof exists; partial failed samples are retained without public figures. An unsupported topology is distinct from an unsupported operation. For the declared unavailable n3 rows in the canonical session contract, a nonzero framework/cleanup exit may be an authentic failure without server/resource/profile evidence only when all seven original unavailable reports, execution7Passed and an actual notExecuted/nonzero-exit qualification receipt are present. This case does not permit a missing TRX or fabricated cases. Corpus SHA256 and payload bytes are not invented: the projection leaves those values unknown.

## Original authority and archive verification

GitHub API captures bind each original ZIP to the repository/source/run/attempt/job/artifact ID and the API digest. All 32 expected worker artifacts are mandatory, including authentic failed/null outcomes. Missing, corrupt, skipped, expired, foreign or mixed evidence is rejected. The number of original input files is derived from the specific authenticated aggregate; there are no fixed totals. A local directory alone does not authenticate GitHub.

The worker `session.json` contains the exact session/target/topology identity, actual image refs, observed DLL/PDB hashes, the target profile/runtime and SHA256 references to the seven original case reports, the unchanged `RuntimeTestResults/<original>.trx`, `runtime-import.json`, `session-qualification.json` and, where applicable, `session-resource-evidence.json`. The original resource sidecar is bound to the original `session-state.json`. container DLL/PDB files are not compared byte for byte with the outer package: they are compiled in a different image build context and carry their own source/image-bound identity.

Preparation retains the original immutable image proof, independent functional cluster proof and `comparison-compiled-runtime-identity`: exactly two original JSON files, `benchmark-runtime.json` and `benchmark-runtime-files.json`. The inventory is bounded to 64 MiB. Descriptor and inventory hashes are bound to each original worker import receipt. Workers download the complete `comparison-compiled-test-bundle` and verify the archive and every file before starting. The collector and Website verify the small identity ZIP and the original metadata of the complete package instead of downloading multi-gigabyte build outputs 32 times.

Bounded REST/rate-limit/header/image APIs are retained. The collector re-verifies the original API-digest-bound ZIPs before aggregation. Website uses confined BCL ZIP preflight/extraction, verifies the original worker/image/runtime/functional archives and the exact extracted SHA256, after which the production projection reads the original reports again. Path escapes, symlinks, duplicate names, substituted hashes/identities, exceeded bounds and mixed attempts are rejected.

## Failure accounting and publication policy

A genuine individual failed case has null metrics; earlier independent measured cases with a valid outcome, profile and resource proof are retained for review. A global post-qualification failure removes every figure of that session. The original qualification receipt contains the actual exit code and two consecutive outer checks: `server-resource-cancellation` and `correctness`. Applicability is fixed: cancellation applies only to KeyLoad n1; correctness applies to KeyLoad/KurrentDB/PostgreSQL/Redis/OpenSearch/MongoDB/Neo4j. The rest are explicitly notApplicable. A supported session with two notApplicable checks receives no invented checks: its individual TUnit workload/oracle results remain mandatory.

`validate` admits complete authentic failed/null accounting. `publish` admits only a successful producer run, all 22 successful worker jobs, passed tests and only measured/unavailable reports. Measured cases additionally require qualified resources and an actual verified profile; failure/notQualified results do not refresh performance evidence. This condition also applies to an independent Website run, not only to the Benchmarks trigger. Permissions are not widened.

The finalizer rejects missing inner original reports. A separate evidence contract for a failure before the workload starts requires a phase/marker ledger and an outer failed TRX; its absence is not replaced with fake cases or unavailable results. Without that contract in source, an early startup failure blocks the complete aggregate.

When no new ready authentic session producer exists, the optional Website publish may build a content-only site without figures. Already selected invalid proof is rejected; there is no fallback to an older cohort or invented metrics. Website/measured/trusted-control source revisions are retained separately. Source configuration, builds or control parser fixtures are not performance measurements or production readiness.

## Projection, browser and resource values

`isolated-projection.mjs` verifies the original session bundle; `isolated-loader.mjs` verifies the hash-bound catalog/projection and bounded HTTP reads. Controls select a case or a single mixed lane, node count 1/3, database, metric and the single original session result. There is no median across repetitions and no pooling across node counts. Mixed streams are not averaged into an artificial overall latency distribution. Unavailable never becomes a numeric zero.

CPU, allocation and peak RSS are process observations over the lane interval; overlapping lanes share one process. Normalizing CPU/allocation by the attempt count of the selected lane does not make them exclusive operation-family costs, and such values must not be summed. The server resource sidecar describes the lifetime of the whole session; intake does not attribute it to an arbitrary individual case.

`isolated-lab.mjs` owns the single visible benchmark surface, cancellation/generations, accessible controls, rows and disposal. `bootstrap.mjs` retains the independent lazy scene lifecycle, reduced motion, fallback and joined cleanup. Official Three.js bytes, license and manifest are not changed.

## TUnit, builder and coverage

All tests are TUnit. `SessionCohortContractTests` and `SiteSessionContractCoverageTests` execute the single controlled `session-contract-probe.mjs`, which imports the actual production modules. Nine scenarios cover plan, reports, selection, environment, qualification, runtime, site-context, publication and profile-accounting. They verify positive inputs and specific identity/accounting/admission mutations; their values never enter published artifacts. Node children have bounded concurrent stdout/stderr, a deadline and joined ownership; Website children inherit the real `NODE_V8_COVERAGE`.

An independent C# oracle computes the expected per-case/lane values from authenticated originals. Real Chrome/CDP sessions load builder-emitted assets through a confined ephemeral HTTP listener and verify controls, keyboard/accessibility, raw hash rejection, cancellation and stale generations. Scene regressions and coverage converter regressions are retained.

Builder entry: `node site/scripts/build.mjs --isolated=<original aggregate directory> --output=<nonexistent isolated output> --revision=<measured SHA> --evidence-url=<exact producer run URL> --site-revision=<qualified website SHA>`. Duplicate/unknown/retired arguments, symlink/path escapes and changed source/vendor bytes are rejected. Original aggregate bytes are retained; the browser receives a compact projection and source-bound static assets, not raw samples. Four shared pure session modules are copied unchanged and verified byte for byte. A failure removes only the newly created owned output.

The trusted-control source closure contains 65 inputs: 61 authored JS sources and 4 PowerShell dependencies; the closure reader verifies the exact sorted manifest and literal transitive imports, and browser roots are listed explicitly. This is source inventory configuration, not a claim of executed coverage. Every required authored JS file without actual execution ranges remains uncovered. Thresholds are not lowered: 80% lines, 70% branches, 90% for critical validators.

GitHub provides the absolute `KEYLOAD_SITE_COVERAGE`, original Node V8 receipts and real Chrome precise-coverage receipts. The immutable source manifest is bound to the actual Website checkout HEAD; source identity alone is not execution proof. Browser URLs `/Features/BenchmarkComparisons/...mjs` and `/scripts/Features/BenchmarkComparisons/...mjs` map to exact repository sources only from allowlisted origins. ranges, stdout and artifact identities are retained; ranges are never fabricated.

Deployment is possible only after Aspire-owned TUnit/analyzer/browser/coverage qualification and exact source/producer freshness, through a needs-gated least-privilege Pages job. Benchmark and Website qualification run only on Linux GitHub Actions.

## range union

Preserve each receipt and script as a separate execution snapshot: raw receipt identity/hash, scriptId, optional execution context, and that script's own functions/ranges. Resolve the deepest physical function and block for a source offset inside one snapshot before taking the covered union across snapshots. Flattening ranges across snapshots is forbidden: a zero-count child in one execution must not override a positive parent in another execution where V8 legitimately omitted that same-count child.

Physical function identity is the exact source path and outer function span; functionName remains metadata and must not create duplicate denominators. The outcome denominator is the union of observed child intervals keyed by source, physical function span and child span. A matching positive explicit outcome proves coverage. When the interval is omitted in another snapshot of the same physical function, infer coverage only when that snapshot proves positive effective execution throughout the canonical interval. An enclosing module is insufficient proof. A positive explicit outer outcome is not invalidated by an unexecuted descendant. Preserve receipts unchanged and record derived decisions separately.

Regression proof uses actual Node-generated receipts from both sides of a real branch. Assert exact executable/covered totals, the true and false return lines covered, an uncalled function uncovered, and identical results after receipt-order reversal. Separate/nested function identity proof also uses real Node output. Malformed/crossing-range cases mutate only the fixture's exact file-URL script, never an unrelated built-in script. Every child process starts bounded concurrent stdout/stderr drains, has a deadline, and joins or kills/joins the owned process in finally. Local development runs remain development evidence; fabricated coverage never qualifies publication.

### equal-span function contract

The [V8 coverage implementation](https://raw.githubusercontent.com/v8/v8/main/src/debug/debug-coverage.cc)
explicitly permits a script wrapper and uncalled inner function with identical
spans; report order is nesting order and the inner function is last.
The converter preserves that order and resolves equal-span ties to the execution
innermost function within each snapshot. This selection applies to line resolution,
explicit outcome checks and omitted-child inference, so script execution cannot
prove an uncalled inner function executed. Names remain metadata and span-based
branch denominators stay unchanged. A real Node single-function source with no
trailing newline proves equal spans/order and uncovered function state;
the Unicode/CRLF9/8line2/2branch and reversed-receipt regressions are retained.

converter regression evidence must survive temporary-directory teardown.
The converter regressions retain unchanged real fixture source, original Node receipts, stdout and
raw SHA256/identity metadata under a separate converter-fixtures subtree of
KEYLOAD_SITE_COVERAGE. These files are uploaded for review and never enter the
production node/ or browser/sessions/ coverage denominators. Preserve original
bytes and cleanup of owned temporary files; mutated malformed/crossing
copies must not be labeled or retained as actual executed evidence.
