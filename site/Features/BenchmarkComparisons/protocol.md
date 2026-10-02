# Static evidence protocol — TASK-SITE-004/005/006

This frozen implementation contract supplements [ADR-040](../../../docs/ADR/ADR-040-static-site-threejs-evidence.md). It does not qualify the run: the authenticated GitHub workflow parent verifies successful main-branch KeyLoad CI evidence before downloading reports.

## Production exports

- `measurements.mjs`: `scenarios`, `colors`, `metrics`, `median(values)`, `selectedRows(report, scenario, repetition, metric)` preserve the existing flat module's output and arithmetic.
- `measurement-loader.mjs`: `validateCatalog(value)` returns the validated object or throws; `validateReport(value, expectedRevision)` returns the validated report or throws; `sha256(bytes)` returns lower-case hex; `loadCatalog({ catalogUrl, signal })` returns the validated catalog; `loadReport({ entry, baseUrl, signal })` verifies exact raw hash, parses/validates and returns the report. Real `fetch` only, no test substitute dependency. `baseUrl` identifies the catalog's data directory.
- `benchmark-lab.mjs`: `mountBenchmarkLab({ root, catalogUrl })` synchronously returns `{ dispose }`, starts loading with owned error handling, clears every dependent surface atomically and rejects aborted/late generations.
- Scene mount is independent, as recorded in the plan. Workers may not change these interfaces without root review.

## Catalog schema 1

`schemaVersion: 1`, ISO `generatedAt`, `siteSourceRevision` (40 lower-case hex or null), `siteSourceKind` (`committed_source` or `local_preview`), `measuredSourceRevision` (40 hex), and `evidenceUrl` (exact `https://github.com/managedcode/KeyLoad/actions/runs/<positive integer>`).

`runs` is a nonempty array with unique lower-case `[a-z0-9-]+` IDs. Each has `id`, nonempty `label`, `report: runs/<id>/results.json`, the same allowlisted `evidenceUrl`, ISO `startedAt`, matching `sourceRevision`, and raw-file `sha256` (64 hex). Every entry revision and evidence URL must equal the catalog values. `local_preview` requires a null website revision; `committed_source` requires a valid website revision. URLs/paths are validated before any link is assigned or report requested. No absolute/parent/query/fragment/encoded paths, foreign origins or partial entries.

## Historical report boundary

Only complete schema-2 reports with the exact six named engines and six named scenarios are accepted. Each engine/scenario/repetition tuple occurs exactly once. Repetitions/options are positive bounded integers (warmup may be zero), source and dataset hashes are valid, dates and nonempty platform/target contract strings are present, and each repetition has exactly 20 supported measured cases and 16 explicitly unsupported cases. Failed reports are rejected for publication/display.

Every measured case has attempts/successes equal to configured operations, zero failures, finite nonnegative metrics and resource measurements, positive elapsed time, consistent percentiles, and exactly one valid successful sample per operation with valid worker/operation IDs. Queue measurements require enqueue/receive/ACK percentile fields and unique-completion count equal to successes. Unsupported cases have null measurements and no samples. Invalid values are rejected, never rendered partly or converted to zero.

## Real Node probe owned by SiteTests

The child process reads one JSON object from stdin and emits exactly one JSON response. `operation` selects:

- `rows`: `reportPath`, `scenario`, `metric`, `repetition`; import production `selectedRows` and return its rows.
- `median`: `values`; invoke production `median` (boundary-value input is legitimate primitive test data).
- `validateCatalog`: `value`; invoke the actual production catalog validator.
- `validateReport`: `value`, `expectedRevision`; invoke the actual production report validator.
- `hash`: `filePath`; hash actual file bytes through production `sha256`.
- `loadReport`: `entry`, `baseUrl`; invoke the actual production `loadReport` with real HTTP against an ephemeral static-file listener serving builder-emitted authentic files. A copied report whose raw bytes change while its catalog hash stays fixed must be rejected. The listener owns a real bound socket and confined file root, bounded requests/cancellation/teardown; it supplies no substitute fetch, validator, arithmetic or invented result.

Response is `{ ok: true, result }` or `{ ok: false, error }`. Validation rejection is a normal captured response; probe/process failure uses nonzero exit and stderr, which the C# process wrapper must treat as a failure. Pass repository root as `KEYLOAD_SITE_REPOSITORY`; resolve actual production module paths from it. The configured absolute `KEYLOAD_SITE_REPORTS` directory contains authenticated downloaded profiles; the run/revision environment inputs identify that receipt, rather than a permanently hard-coded run or download directory. The committed-site builder case uses GitHub's actual `GITHUB_SHA`, proving that website and measured revisions occupy separate catalog fields and documentation links address the website source. No arithmetic or validator implementation in the probe. SiteBuildTests invoke the real `site/scripts/build.mjs` CLI directly against isolated temporary output, checking exit/error behavior and complete raw-byte-preserving assets. C# independently computes numeric expectations over authentic downloaded GitHub reports. Controlled malformed copies are never published.

## Root builder CLI

`node site/scripts/build.mjs --reports=<downloaded directory> --output=<isolated nonexistent output> --revision=<measured SHA> --evidence-url=<successful run URL> [--site-revision=<committed website SHA>]`.

All evidence arguments are mandatory. Omitting `--site-revision` produces an explicitly uncommitted local preview. Existing output must be refused (clean-output contract); callers use disposable distinct paths. Refuse output equal to/inside source or reports or their ancestors, filesystem root, and symlinked input/output escapes. Copy exact report/CSV/Markdown bytes. Validate the complete input and vendor manifest before creating output; on failure remove only newly owned output. Root may add helper modules only with an explicit ownership/plan update.

## Native coverage receipt — TASK-SITE-008/009

GitHub provides absolute `KEYLOAD_SITE_COVERAGE`; normal Node children inherit `NODE_V8_COVERAGE=<root>/node`. TUnit's before-session hook writes `<root>/source-manifest.json` with immutable source revision and raw SHA256 inventory; the after-session gate joins actual Node and browser coverage, verifies unchanged source bytes and writes `<root>/report.json` before enforcing ADR-040 thresholds. Root provides the actual `GITHUB_SHA`, Node version and system Chrome executable/version receipts. No local qualification.

Each browser invocation owns a fresh `<root>/browser/sessions/<lowercase unique id>/` directory and emits `metadata.json` there with exact fields:

`{schemaVersion:1,sourceRevision:<GITHUB_SHA>,browserVersion:<actual Chrome version>,origins:[<actual http://127.0.0.1:port origins>],sources:[{path:<repository-relative site/... path>,sha256:<source-manifest hash>}],coverageFiles:[<native JSON basenames>]}`.

The sources list contains the exact 13 authored production paths from the source manifest, including Node-only build sources; this proves identity and is never proof of execution. Unique origins/source paths/file names, valid raw hashes and confined names are required. Metadata is written only after all owned browser commands/process outputs settle. Session directories prevent parallel test overwrites. Missing/malformed metadata or a requested native coverage file fails the gate.

Each listed native JSON file is the unchanged command-result object from `Profiler.takePreciseCoverage`, including its optional official timestamp: `result` contains script records with `scriptId`, `url`, `functions`, and each function's `functionName`, `isBlockCoverage` and `ranges`. `isBlockCoverage` belongs to the function, not its ranges. Keep supported API fields; do not invent ranges or transform the raw receipt. Before page script execution enable Profiler and start precise coverage with callCount/detailed true. Browser scripts at an allowlisted origin's `/Features/BenchmarkComparisons/...mjs` map to `site/Features/BenchmarkComparisons/...mjs`; official unchanged vendor and nonproduction eval scripts do not enter the denominator. Node file URLs map only to exact required repository source files. Every required authored file missing actual execution ranges is uncovered and fails; source identity metadata cannot make it covered.

## Native range union repair — TASK-SITE-013

Preserve each native receipt and script as a separate execution snapshot: raw receipt identity/hash, scriptId, optional native execution context, and that script's own functions/ranges. Resolve the deepest physical function and block for a source offset inside one snapshot before taking the covered union across snapshots. Flattening ranges across snapshots is forbidden: a zero-count child in one execution must not override a positive parent in another execution where V8 legitimately omitted that same-count child.

Physical function identity is the exact source path and outer function span; functionName remains metadata and must not create duplicate denominators. The outcome denominator is the union of observed native child intervals keyed by source, physical function span and child span. A matching positive explicit outcome proves coverage. When the interval is omitted in another snapshot of the same physical function, infer coverage only when that snapshot proves positive effective execution throughout the canonical interval. An enclosing module is insufficient proof. A positive explicit outer outcome is not invalidated by an unexecuted descendant. Preserve native receipts unchanged and record derived decisions separately.

Regression proof uses actual Node-generated receipts from both sides of a real branch. Assert exact executable/covered totals, the true and false return lines covered, an uncalled function uncovered, and identical results after receipt-order reversal. Separate/nested function identity proof also uses real Node output. Malformed/crossing-range cases mutate only the fixture's exact file-URL script, never an unrelated built-in script. Every child process starts bounded concurrent stdout/stderr drains, has a deadline, and joins or kills/joins the owned process in finally. No local tests or fabricated native coverage qualification.

### Native equal-span function contract

The [V8 native coverage implementation](https://raw.githubusercontent.com/v8/v8/main/src/debug/debug-coverage.cc)
explicitly permits a script wrapper and uncalled inner function with identical
spans; native report order is nesting order and the inner function is last.
TASK013 must preserve that order and resolve equal-span ties to the native
innermost function within each snapshot. Apply this selection to line resolution,
explicit outcome checks and omitted-child inference, so script execution cannot
prove an uncalled inner function executed. Names remain metadata and span-based
branch denominators stay unchanged. A real Node single-function source with no
trailing newline proves native equal spans/order and uncovered function state;
retain the existing Unicode/CRLF9/8line2/2branch and reversed-receipt regressions.
Only source review/build and exact-SHA GitHub execution can join this correction.

Native converter regression evidence must survive temporary-directory teardown.
TASK013 retains unchanged real fixture source, original Node receipts, stdout and
raw SHA256/identity metadata under a separate converter-fixtures subtree of
KEYLOAD_SITE_COVERAGE. These files are uploaded for review and never enter the
production node/ or browser/sessions/ coverage denominators. Preserve original
native bytes and cleanup of owned temporary files; mutated malformed/crossing
copies must not be labeled or retained as actual executed native evidence. Root
reviews the retained packet and actual GitHub converter assertions before join.
