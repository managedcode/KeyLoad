# Fresh evidence publication acceptance

Date: 2026-10-02. Canonical slice: BenchmarkComparisons. Stable requirement:
REQ-BC-028; criterion: AC-BC-028. [Brainstorm](site-publication.brainstorm.md),
[feature](docs/Features/BenchmarkComparisons.md), [ADR-040](docs/ADR/ADR-040-static-site-threejs-evidence.md).
Strongest read-only planning is COMPLETE; recorded-contract approval is required
before write-capable publication workers. Earlier site criteria remain mandatory.

## Outcome, actors and boundaries

All website performance figures/charts are computed from authentic JSON artifacts
of the actual GitHub comparison job. A separate pages.yml selects fresh evidence,
qualifies the complete site and publishes only after every required gate passes.
Actors: comparison producer, trusted Actions control workflow, site qualification,
reader and deploy job. Entry points: main push changing site/**, completion of the
main KeyLoad CI producer workflow, or explicit manual validate/publish dispatch.
workflow_run fires after the enclosing workflow, not immediately after one job.
No CI producer/runtime/API/schema/credential/DNS/
README-chart-branch changes. Unsupported schema3 is rejected, never dropped or
converted into invented data. Existing site/report schema2 is the accepted format.

Selection/qualification use contents/actions read only. Deploy alone receives
pages write/id-token write in github-pages. Control source is github.workflow_sha;
publication site source is the current trusted main website SHA. Separate sibling
website/measured/control checkouts preserve measured-source inspection without
building or qualifying the database. Record site/measured/control revisions
independently; site and measured may differ. Candidate validate cannot deploy.
No branch manual publish or historical publication pin is allowed. The owner's
2026-10-02 site-only instruction supersedes the earlier equality/conservative-B
task contract; preserved policy conflicts are explicitly recorded in ADR-040.

## AC-BC-028 measurable pass/fail conditions

1. pages.yml has one select→qualify→deploy chain; remove the duplicate route that
   bypasses qualification. Every existing Analyzer/native80/70/90, complete TUnit,
   real Chrome, JS80/70/90, format and governance gate remains. Any missing/skipped/
   failed gate prevents deployment and leaves the previous public output.
2. Capture workflow, paginated main-push runs, selected run, exact-attempt jobs and
   run-bound artifacts through authenticated GitHub REST. Retain original bytes
   and hashes. Complete counts, unique identities, active ci.yml workflow, own
   repository/head repository, main/push, exact workflow ID/path/SHA/attempt and
   successful comparison-job execution are mandatory. File shape validation alone is not API
   authentication; only the real workflow establishes transport provenance.
3. Validate may pin an authenticated historical run or choose latest comparison.
   Publish enumerates own-main-push runs by descending run_number and attempts
   descending from actual run_attempt to1. Retain exact authenticated attempt and
   jobs captures for each visited candidate. Missing/pending/cancelled/failed
   comparison jobs continue to the next attempt/run. Select the highest successful
   comparison; once selected, missing/expired/ambiguous artifact or invalid reports
   FAIL there, never fall back. Inaccessible required history fails closed. This
   preserves an earlier successful attempt after a failed partial rerun, assuming
   Actions history is retained; administrative deletion has no monotonic guarantee.
   Before deployment repeat selection and recheck current trusted-main site SHA
   and exact measurement tuple; any change is superseded and needs qualification.
   Record check time; do not claim an atomic lock over concurrent CI completion.
4. Require exactly one successful exact-attempt comparison-smoke with three unique
   successful measurement steps: the current
   unnamed ComparisonTests command, the named1KiB/eight-client step and named16KiB/
   four-client step specified in ADR-040. Preserve actual step numbers. Reject
   failed/skipped/missing/ambiguous selected steps, wrong attempts and SHA. Other
   jobs and whole-workflow conclusion do not qualify or block comparison evidence.
   Labels say GitHub comparison evidence, never falsely call a failed run successful.
5. Require exactly one nonexpired comparison-suite with positive size≤128MiB,
   sha256 digest, complete matching workflow_run identity and creation within the
   successful comparison job interval. No invented job_id/run_attempt artifact
   fields. Download its immutable ID once; retained ZIP SHA and length must match
   authenticated metadata before qualification. Corruption never becomes input.
6. Mandatory TUnit before-session preparation uses that same retained ZIP through
   BCL ZipArchive; no pre-extracted/fallback path. Preflight the entire set before
   creating its nonexistent owned destination: exactly12files, three known profiles
   × results.json/samples.csv/results.md/runner.log, optionally unique empty profile directory
   entries. Reject unexpected/duplicate/aliased/case-alias/absolute/backslash/
   traversal/link paths, nonempty directory entries and invalid bounds. File≤8MiB,
   total uncompressed≤64MiB; enforce declared and streamed bounds. Hash ZIP before/
   after the same owned stream and retain raw file SHA/length receipt outside JS
   coverage. Retain/hash all12 inputs; publish only the existing9 JSON/CSV/MD reports.
   Logs never supply performance values. Failures leave no partial destination and preserve unrelated files.
7. After tests and before/after final build, verify extracted bytes against that
   receipt; compare all final emitted raw JSON/CSV/MD bytes as well. No workload
   value is embedded in site source or introduced by metadata/ZIP helpers. Existing
   strict report/revision/hash validation and numerical/browser assertions remain.
8. KEYLOAD_SITE_SOURCE_REVISION must match actual checkout HEAD before source
   manifests/native qualification. Site inputs, JS coverage and analyzer receipts
   use this actual SHA rather than unchanged triggering GITHUB_SHA after checkout.
   Retain triggering/control SHA separately. All four new Node gate modules enter
   the closed authored source inventory and individual critical90 line gates;
   every existing source/denominator remains. No invented baseline/exclusion.
   Native PowerShell uses one Get-CoverageSourceRevision resolver: presence of
   KEYLOAD_SITE_SOURCE_REVISION selects it exclusively and invalid/empty values
   fail. Only truly absent dedicated input permits strict GITHUB_SHA for unchanged
   non-Pages callers. Verify resolves inside try from null so failures cannot
   falsely inherit trigger SHA. Real child-process-only environment regressions
   cover distinct dedicated/trigger SHA, absent compatibility, malformed/no input,
   and changed dedicated revision between Prepare/Verify. SiteTests have no fallback.
9. Retain full qualification receipt and publish data/publication.json containing
   actual site/measured/control revisions, measurement run number/attempt/job URL,
   artifact ID/digest, raw report hashes and qualification run/job URLs. Publish
   only the exact qualified site artifact. Preserve raw/download/run provenance;
   successful build/source review alone is not a Pages provider/live result.

## Frozen Node CLI and input/output contract

New production files, only under scripts/Features/BenchmarkComparisons/:
github-evidence-contracts.mjs (named fields/limits/errors), github-evidence-runs.mjs
(pagination/selection), github-evidence-proof.mjs (job/artifact/digest/freshness),
github-evidence.mjs (bounded CLI/local I/O; no HTTP, ZIP parsing or arithmetic).
Capture directory contains workflow.json, runs-pages.json, selected run.json,
jobs-pages.json, artifacts-pages.json and search trail
attempts/<runId>/<attempt>/{run.json,jobs-pages.json}. `select` reads workflow/runs
and existing trail captures. Missing both next-attempt files returns needs_attempt;
one missing or malformed capture fails. Root fetches authenticated exact endpoints
/actions/runs/{id}/attempts/{n} and /attempts/{n}/jobs, then calls select again.
The bounded loop continues until selected/unavailable. `prove` requires all five
selected captures and the complete search trail and replays the same selection.
Selected run.json is the actual attempt response, not current aggregate conclusion.
Paginated responses are unchanged
`gh api --paginate --slurp` arrays. Reject ambiguous/incomplete metadata.

- select --input=<absolute capture-dir> --mode=validate|publish [--requested-run=<id>]
- prove --input=<capture-dir> --mode=... --site-revision=<sha> --workflow-revision=<sha> [--requested-run=<id>]
- verify-archive --receipt=<metadata receipt> --archive=<zip>
- fresh --before=<archive-verified receipt> --after=<new metadata receipt>

One JSON envelope per invocation: {ok:true,result:...} or
{ok:false,error:{code,message}} with nonzero exit for invalid input. Stable errors:
E_ARGUMENT/E_CAPTURE/E_PAGINATION/E_WORKFLOW/E_RUN/E_JOB/E_ARTIFACT/E_ARCHIVE/
E_SOURCE/E_FRESHNESS. Legitimate publication blocks explicitly return a
non-deployable result; they cannot masquerade as qualified evidence.

Receipt schema1 distinguishes metadata_verified/archive_verified and contains
`mode` (validate or publish) and `publishEligible` boolean,
repository fullName/id, workflow id/path, run id/number/attempt/url,
siteSourceRevision/measuredSourceRevision/controlWorkflowRevision, comparisonJob
id/url/startedAt/completedAt plus successful measurement step name/number entries,
artifact id/name/sizeBytes/digest/createdAt, and metadataFiles path/sha256 for the
five original captures and every required trail capture. Archive verification additionally records
`archive:{sha256,bytes}` from the actual retained ZIP. The standalone receipt file
is the envelope's `result`, not the envelope itself. `select` returns
state=needs_attempt with runId/runNumber/runAttempt/measuredSourceRevision; or
state=selected with those fields, comparisonJobId, publishEligible and reason=null;
or state=unavailable with publishEligible=false and named reason=no_successful_comparison.
Set publishEligible only for publish mode with no historical pin. Exhausted or
blocked input never becomes a proof. Bound selection to≤1000 fully enumerated runs
and≤10000 distinct (runId,attempt) pairs, each containing two required capture
files; each metadata file≤16MiB. Root collection permits at most10000 exact-pair
fetch iterations plus terminal select, with bounded network timeouts. Breaches
fail explicitly, never truncate history or fall back.
Freshness requires before state=archive_verified and after state=metadata_verified,
both mode=publish/publishEligible=true and exactly equal siteSourceRevision values.
The after receipt is freshly produced by `prove` against a newly authenticated
capture. Compare the complete measurement tuple; never trust a supplied URL as
authentication or accept a historical validate receipt as publication eligibility.
No token, process environment or fabricated provider field appears in receipts.

Root-owned qualification environments: KEYLOAD_SITE_GITHUB_CAPTURE is the absolute
capture directory; KEYLOAD_SITE_ARCHIVE is the retained ZIP;
KEYLOAD_SITE_ARCHIVE_RECEIPT is its standalone archive_verified result JSON;
KEYLOAD_SITE_REPORTS is the nonexistent owned extraction destination;
KEYLOAD_SITE_SOURCE_REVISION is actual checkout HEAD. Existing KEYLOAD_SITE_COVERAGE
identifies the native evidence directory. Setup persists a schema1 extraction
receipt under its parent's archive-inputs/extraction.json with archive SHA/bytes,
reportsRoot and files[{path,sha256,bytes}]. No environment fallback or existing
reports destination is accepted. ArchiveSetup exposes real
PrepareAsync(archivePath,receiptPath,destination,evidenceRoot,cancellationToken)
and environment preparation; root owns mandatory hook wiring. Final raw recheck
uses that returned/retained typed receipt, not fresh invented expected hashes.

## Required test matrix and operational evidence

TASK021's real bounded Node TUnit tests use the actual authenticated captures and
ZIP, then controlled metadata mutations: positive/shuffled order; incomplete/
duplicate pagination; wrong repository/workflow/event/main/SHA/attempt; failed/
skipped/missing/duplicate jobs/steps; missing/expired/ambiguous/wrong-run/timestamp/
digest artifacts; corrupt ZIP; publication pins; job success with unrelated job
failure; newer pending/cancelled/failed comparisons; missing partial-rerun jobs;
earlier successful attempts; required missing history; changed site/attempt/job/
artifact/digest. Controlled inputs prove validator
behavior, never fake API integration or publication success.

TASK022's real BCL tests use authentic ZIP bytes and controlled malformed copies:
complete exact extraction and independent hash oracle; duplicate/unexpected/
aliased/link/traversal/absolute/backslash paths, invalid directory/file/stream
bounds, corrupt/missing archive and preexisting output/sentinel preservation.
Actual helper/setup is exercised, not a copied parser or substitute archive service.
Every malformed case identifies its intended rejection; controlled copies may
recompute their own hash to reach the actual entry/bounds check, but remain
rejection-only data and never authenticated provider evidence. A digest failure
must not falsely pass traversal/duplicate/bounds acceptance tests.
Root integration tests retain all existing authentic numerical/browser assertions,
verify source SHA and final raw/provenance files, and enforce all numeric gates.

GitHub-only full suite is the automated qualification. Actual workflow_run wake-up,
permissions/API transport/cross-job transfers/predeploy recheck/provider deployment
and live publication.json/report/UI matching require real run/job/artifact/provider/
browser evidence. These are explicit operational evidence requirements, not waived
criteria. Historical successful comparison evidence may qualify a current website;
it never establishes current database/runtime/performance implementation readiness.
No current publication readiness is claimed before actual site/provider proof.

No database/data migration. Rollback restores the coherent prior workflow/site
source, leaves the last published verified data and immutable evidence, and does
not restore a bypass of mandatory gates. Missing credentials, runner facilities,
authentic inputs or qualified main source are exact blockers, never fake success.

### Final integration invariants for AC-BC-028

- The four executed control metadata modules and the authenticated collector MUST
  match the independently qualified website source byte for byte. Record their
  hashes; source drift fails before qualification or deployment.
- The final builder recheck validates the extraction receipt schema, archive
  identity, exact reports root and twelve unique canonical paths before reading
  the retained inputs. Materialize the twelve verified rows successfully; no
  failing process substitution or empty inventory may count as passing.
- Metadata tests derive expected run, attempt, job, step, artifact, file-count and
  ZIP identities from their retained authenticated inputs and the actual ZIP.
  Historical IDs, step ordinals or digests MUST NOT prevent a newer valid producer
  result from qualifying. Controlled copies exercise named rejection and history
  edge cases only and never count as authenticated provider results. Required
  steps are located and mutated by exact name, not incidental array position.
- Archive preparation accepts exactly the consistent validation/non-publishable
  and publication/publishable receipt modes. Validation still cannot deploy.
- Exactly one positive TRX per complete suite must prove total=executed=passed
  with no skipped work; retain counts and hashes with the publication provenance.
  A Pages receipt records actual provider outcome and returned URL, including
  skipped or failed outcomes without inventing a successful deployment.

The optional-directory archive edge case uses a controlled valid-format copy:
retain all twelve authentic file bytes/hashes and add only a unique empty profile
directory. This tests BCL input acceptance and is explicitly not authenticated
provider evidence. All malformed archive copies remain rejection-only. Streamed
size tests use a real BCL stored ZIP containing those file bytes, corrupt only a
known central-directory uncompressed length, recompute the rejection-copy digest,
and require the actual preparation API to reject the stream with no owned output.
Post-extraction mutation of a confined copied input must fail the real unchanged
verification. These cases complete the existing AC028 edge/error matrix.

The final strongest source review identified two malformed-input obligations
within AC-BC-028/2,4,5,9. TASK030 retains the existing four-module inventory:
every flattened jobs/artifacts page requires positive unique IDs, including
nonselected entries; receipt verification requires exactly the three unique
named successful measurement-step records with distinct positive step numbers.
Metadata receipts require unique canonical capture paths and lowercase64 SHA256,
all five top-level capture files and the selected attempt run/jobs pair. Extra
attempt trail records must have their matching pair; arbitrary or traversal paths
fail. Job/artifact timestamps must parse and preserve start≤created≤completion;
artifact/archive byte counts must be positive and within128MiB, with exact digest
and byte agreement. Empty/stripped receipts never satisfy archive or freshness
checks. Existing command/error families and valid-mode contracts remain stable.
TASK029 first adds real-CLI regressions; the next exact-SHA GitHub run establishes
their red baseline before030 repair. Controlled mutations remain rejection data,
never provider authentication. Root joins all sources, strongest review and the
complete native/Chrome/17-source9critical gates without changing thresholds.

AC-BC-014/025 additionally requires a real emitted-page QueueCycle ACK→PointRead
transition to reset the metric to the existing default throughput, disable queue
metrics, and render the independent actual PointRead oracle without browser
exceptions. TASK032 adds the regression before the exact-SHA red GitHub run and
repairs the existing undefined-symbol reference afterwards. Every earlier browser
assertion and source/coverage threshold remains mandatory.
