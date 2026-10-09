# Current native comparison website protocol

This implementation contract supplements [ADR-040](../../../docs/ADR/ADR-040-static-site-threejs-evidence.md), [ADR-056](../../../docs/ADR/ADR-056-isolated-linux-comparison-cells.md) and [ADR-076](../../../docs/ADR/ADR-076-current-cohort-publication.md). The current composite Benchmarks comparison-aggregate job is the sole live producer. Current profile/catalog collectors and renderers consume only the authenticated schema-5 cohort. Unsupported producer revisions are unavailable by identity and are never interpreted or converted.

## Original authority and production exports

The authenticated workflow captures the exact source/run/attempt/job/artifact tuple and original provider archives. Confined BCL extraction validates both immutable ZIPs, the original bounded receipt and all 1,716 original inputs (1,656 suite files and 60 provider files) for the complete current 924-worker native cohort. Owner correction2026-10-04 (ADR-080) allows authenticated terminal failed cells with a fixed safe reason and null report; original workload/job failures remain visible and result upload must succeed. Missing, skipped, expired, mixed or unauthenticated evidence fails closed. The complete planned-cell inventory remains mandatory. If no cell has measured data, datasetSha256 is explicitly null. Website, measured and trusted-control source revisions remain distinct and accurate. A supplied local directory cannot authenticate GitHub.

- `isolated-projection.mjs` consumes the complete real aggregate and validates every source record before emitting the browser projection.
- `isolated-loader.mjs` validates the catalog and complete projection, verifies raw SHA256 before parsing and uses real bounded HTTP reads with cancellation. Foreign URLs, unsafe paths, invalid metadata and incomplete bodies fail explicitly.
- `isolated-measurements.mjs`, `measurements.mjs` and `isolated-controls.mjs` retain native dimension selection, metric arithmetic, median, percentile/resource units and honest unsupported values. An unavailable value is never converted to zero.
- `measurement-loader.mjs` retains only the shared cross-runtime SHA256 primitive. The browser path uses its native crypto API; Node-only imports cannot occur on the browser path.
- `isolated-lab.mjs` owns the sole visible `#benchmarks` surface, bounded request generations, dependent controls, accessible rows and disposal. Abort and late results cannot replace the selected generation.
- `bootstrap.mjs` mounts the current comparison surface and preserves copy behavior, independent lazy scene lifecycle, reduced motion, page lifecycle, fallback and teardown. Official Three.js bytes, license and manifest stay unchanged.

## Real probes and client behavior

TUnit owns all site qualification. Real Node child probes import the actual production arithmetic and validators; C# derives an independent expectation from authenticated original current records. Controlled malformed copies may prove rejection but cannot be published or labeled genuine measurements. Probe responses are one bounded JSON `{ ok: true, result }` or `{ ok: false, error }`; child failure, nonzero exit, stderr, timeout or incomplete drains fail the test. Every child joins or kills/joins its owned process in finally.

Real Chrome/CDP sessions exercise builder-emitted assets through an actual confined ephemeral HTTP listener. Preserve raw-hash rejection, all current engine/node/scenario/repetition/metric selection, independent arithmetic, keyboard/accessibility, cancellation and stale generation behavior. Scene tests retain pointer following, pause/resume, reduced motion, retina resizing, lazy rendering, visibility/page lifecycle and safe fallback. No substituted fetch, fake browser, invented samples or fabricated coverage.

## Root builder CLI and final publication receipt

`node site/scripts/build.mjs --isolated=<original aggregate directory> --output=<isolated nonexistent output> --revision=<measured SHA> --evidence-url=<exact producer run URL> --site-revision=<qualified website SHA>`.

All arguments are mandatory; duplicate/unknown options and retired `--reports` input fail. Validate the complete source identity, aggregate/projection, vendor manifest/bytes and authored gzip budgets before creating output. Refuse existing output, source/input ancestors or descendants, filesystem root and symlinked input/output escapes. Preserve exact `aggregate.json` bytes; emit only the validated current catalog/projection and source-bound static assets, metadata/icons/SEO, never raw benchmark samples. On failure remove only newly owned output.

The workflow snapshots a regular original archive receipt of at most 4 MiB into a read-only owned file and verifies its hash, original archives and all 1,716 input hashes before and after the bounded builder. Emitted aggregate bytes must match exactly. `data/publication.json` schema 2 records website/measured/control revisions, original test/coverage/job receipts and complete isolated archive authority. Every current authored JS source remains in native coverage: aggregate lines 80%, branches 70%, critical validators 90%. Trusted-control source closure covers both current producer tools and their actual builder/browser consumers. No threshold is waived by retiring a dead source.

Deploy only after complete Aspire-owned TUnit/analyzer/native-browser qualification and exact main/producer freshness, through the needs-gated least-privilege Pages job. Preserve the real provider outcome receipt. Configuration and source checks alone are not publication or database qualification.

## Native coverage receipt — TASK-SITE-008/009

GitHub provides absolute `KEYLOAD_SITE_COVERAGE`; normal Node children inherit `NODE_V8_COVERAGE=<root>/node`. TUnit's before-session hook writes `<root>/source-manifest.json` with immutable source revision and raw SHA256 inventory; the after-session gate joins actual Node and browser coverage, verifies unchanged source bytes and writes `<root>/report.json` before enforcing ADR-040 thresholds. Root provides `KEYLOAD_SITE_SOURCE_REVISION` bound to the actual website checkout HEAD, and separate triggering/control revisions, Node version and system Chrome executable/version receipts. No local qualification.

Each browser invocation owns a fresh `<root>/browser/sessions/<lowercase unique id>/` directory and emits `metadata.json` there with exact fields:

`{schemaVersion:1,sourceRevision:<GITHUB_SHA>,browserVersion:<actual Chrome version>,origins:[<actual http://127.0.0.1:port origins>],sources:[{path:<repository-relative site/... path>,sha256:<source-manifest hash>}],coverageFiles:[<native JSON basenames>]}`.

The sources list contains every current authored production path from the immutable source manifest, including Node-only build sources; this proves identity and is never proof of execution. Unique origins/source paths/file names, valid raw hashes and confined names are required. Metadata is written only after all owned browser commands/process outputs settle. Session directories prevent parallel test overwrites. Missing/malformed metadata or a requested native coverage file fails the gate.

Each listed native JSON file is the unchanged command-result object from `Profiler.takePreciseCoverage`, including its optional official timestamp: `result` contains script records with `scriptId`, `url`, `functions`, and each function's `functionName`, `isBlockCoverage` and `ranges`. `isBlockCoverage` belongs to the function, not its ranges. Keep supported API fields; do not invent ranges or transform the raw receipt. Before page script execution enable Profiler and start precise coverage with callCount/detailed true. Browser scripts at an allowlisted origin's `/Features/BenchmarkComparisons/...mjs` map to `site/Features/BenchmarkComparisons/...mjs`; official unchanged vendor and nonproduction eval scripts do not enter the denominator. Node file URLs map only to exact required repository source files. Every required authored file missing actual execution ranges is uncovered and fails; source identity metadata cannot make it covered.

## Native range union repair — TASK-SITE-013

Preserve each native receipt and script as a separate execution snapshot: raw receipt identity/hash, scriptId, optional native execution context, and that script's own functions/ranges. Resolve the deepest physical function and block for a source offset inside one snapshot before taking the covered union across snapshots. Flattening ranges across snapshots is forbidden: a zero-count child in one execution must not override a positive parent in another execution where V8 legitimately omitted that same-count child.

Physical function identity is the exact source path and outer function span; functionName remains metadata and must not create duplicate denominators. The outcome denominator is the union of observed native child intervals keyed by source, physical function span and child span. A matching positive explicit outcome proves coverage. When the interval is omitted in another snapshot of the same physical function, infer coverage only when that snapshot proves positive effective execution throughout the canonical interval. An enclosing module is insufficient proof. A positive explicit outer outcome is not invalidated by an unexecuted descendant. Preserve native receipts unchanged and record derived decisions separately.

Regression proof uses actual Node-generated receipts from both sides of a real branch. Assert exact executable/covered totals, the true and false return lines covered, an uncalled function uncovered, and identical results after receipt-order reversal. Separate/nested function identity proof also uses real Node output. Malformed/crossing-range cases mutate only the fixture's exact file-URL script, never an unrelated built-in script. Every child process starts bounded concurrent stdout/stderr drains, has a deadline, and joins or kills/joins the owned process in finally. Local development runs remain development evidence; fabricated native coverage never qualifies publication.

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
