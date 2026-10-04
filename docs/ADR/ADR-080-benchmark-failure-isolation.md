# ADR-080: Isolate benchmark failures during publication

Status: Accepted; source implemented, delivered-source verification pending.
Date: 2026-10-04. Related: REQ/AC-BC-FAIL-001..011, ADR-056/074/076.

## Decision

The owner requires all independent benchmarks to finish and publication to proceed
when one database workload fails, including unfinished KeyLoad. Preserve every
planned cell, actual native topology, bounded resources, workload contracts and
source/run/attempt/job/artifact authentication. A failed workload emits a version4
envelope with `disposition: failed`, a fixed safe reason and `report: null`. Its
original job remains failed and workload step remains failure; result upload must
succeed. Measured envelopes require successful workload jobs. Evidence mismatch,
missing artifacts, mixed sources, failed image preparation or unsuccessful site
qualification still fails publication. Partial success cannot establish a winner
against an unavailable database. No engine implementation or persisted data changes.

The alternative of stopping all publication discards useful independent results.
Treating failures as zero would invent comparative performance. Retaining raw
exception text risks exposing credentials; use a fixed public reason and link the
original GitHub job for diagnostics. Ordinary runner failures are finalized before
upload; cancellation/timeouts that prevent artifacts remain explicit blockers.

## Implementation contract

1. Root records owner policy, feature acceptance, exact baseline and shared schema.
2. Producer worker owns aggregation/GitHub selection modules and producer regression
   tests; preserve original step conclusions and require envelope/proof agreement.
3. Site worker owns isolated browser contract/metadata/report/numeric/view modules
   and dedicated TUnit projection/oracle/browser regressions. Failed cells contain
   no numeric metrics and retain their failed job links.
4. Root owns workflow always-finalization/aggregate condition, shared receipt
   validation, dependency/coverage inventory, source name tests and documentation.
   Diagnostic worker inspects native startup errors without changing KeyLoad engine.
   Under FAIL-PREP-REGISTRY, the existing image-contracts/manifests/evidence modules
   own2s real HTTP probes inside the unchanged30s readiness bound and private
   no-follow121-record/64KiB probe facts. Evidence I/O errors escape transport
   retry handling. Ten actual HTTP fixture cases belong to UnitTests; the image
   preparation job must build that runner and invoke its Aspire-owned filter.
   Run these actual loopback fixture tests before `prepare-images.mjs` owns the
   same registry port; restore/build the native test runners first. Keep the
   actual pinned image export/import checks after image construction.
   The readiness helper accepts an optional numeric loopback port with default
   registry.port; validate integer1..65535 before URL construction, HTTP or
   evidence I/O. Both production callers retain their no-argument default5000.
   Actual HTTP fixtures listen on kernel-assigned port0, retain that listener
   until cleanup and pass only its observed port. Extend the existing bounded
   evidence case with invalid-number/type checks, zero HTTP requests and absent
   evidence assertions; do not change OS settings/processes or accept arbitrary
   hosts/URLs/environment overrides. Root integrates this port-isolation follow-up,
   reviews the unchanged production callers and reruns the10 original scenarios
   through freshly built Aspire/TUnit before the next scoped checkpoint.
   Under FAIL-PREP-REDIS (REQ/AC-BC-FAIL-009), preserve the existing isolated
   password-authenticated RESP/TCP bootstrap and native6379 replication. Use the
   official resource-local `WithoutHttpsCertificate()` call, with compiler opt-in
   ASPIRECERTIFICATES001 limited to the native API and direct annotation checks.
   This prevents the pinned Aspire13.6 BeforeStart callback from changing the
   endpoint/discovered connection to TLS while the owned bootstrap stays plain.
   Native-failures agent owns only IsolatedRedisResources.cs, the existing Redis
   resource-model regression and actual RedisNativeReadinessRegression transport
   checks. Root owns docs, integration, build/format/Aspire verification, scoped
   checkpoint and genuine new-source1/2/3-node preflight/publication evidence.
   Resource tests require explicit certificate opt-out, fixed native port/scheme
   and authenticated non-TLS client configuration; real startup tests also retain
   PONG/write/direct-copy/cancellation and AOF/ack contracts. Never remove health
   checks, alter shared TLS/trust settings, disable certificate validation or
   change another engine's resources. Rollback is the scoped call/check removal
   and honestly restores the known readiness mismatch; no storage migration.
   Under FAIL-PREP-KURRENT, `KurrentTarget.InitializeAsync` constructs the actual
   SDK writer only after `KurrentClusterVerifier.VerifyAsync` proves membership.
   Three SDK/resource-model tests belong to ComparisonTests and run in common
   preparation; existing native StreamAppend preflights qualify1/2/3-node semantics
   and copy/cleanup behavior. No provider replacement, URI or ACK/retry change.
   Under FAIL-PREP-OPENSEARCH (REQ/AC-BC-FAIL-010), retain the existing
   float32 vector corpus, shared double cosine oracle, exact projection and
   ordinal ID tie order. The original genuine n1 job rejects the same query
   twice in each repetition; it remains failed/null in its original publication
   cohort. A float translated-score witness demonstrates that distinct exact
   scores can collapse and reverse ID order, but does not establish unrecorded
   native returned IDs. Pinned3.6.0/k-NN3.6.0.0 allows vector doc values in
   scripted-metric contexts but not NumberSortScript; generic vector get(int)
   throws. Use the supported native scripted_metric map/combine/reduce path
   over every eligible indexed vector, retaining at most TopK native candidates
   per shard and merging only those bounded states. Preserve double cosine
   descending and ordinal ID ascending. Read the existing native source payload
   only for admitted candidates; do not duplicate stored vectors or fetch/rerank
   in the client. Convert query
   parameter elements through float32 before double arithmetic to match stored
   float32 inputs. Keep finite response validation, actual native index/copies,
   cancellation and existing workload bounds; preserve non-vector response checks.
   Do not change the shared oracle, corpus, ANN mode, client reranking, TLS or
   acknowledgements. Record the native exact-double strategy in its report
   contract. Native-failures agent owns only the OpenSearch query/script/response/
   session/probe/names helpers and focused ComparisonTests precision/query/response regressions;
   root owns contract/docs, integration/build/format/Aspire checks and delivery.
   Tests must retain the independent d170/2289/1272 precision witness, native
   response rejection cases and real VectorExact1/2/3 jobs with all50000 measured
   operations per cell passing the unchanged oracle. Current run continues to
   publish honest unavailable cells; new source must retain its own entire
   authenticated270 cohort and full site/Pages gates. Rollback reverts only these
   benchmark adapter/tests/contracts, restoring the known precision rejection;
   no database migration or engine repair is involved.
   Under FAIL-SITE-STARTUP (REQ/AC-BC-FAIL-011), original270 aggregation passed
   but QualifySite shell redirection failed before any test because its evidence
   parent was absent. Root owns QualifySite/action.yml initialization, the new
   mandatory Aspire unit preparation step, closed workflow gate tests and docs.
   Create only workspace/artifacts/site-evidence after rejecting file/symlink
   collisions at its two owned path components. Preserve the exclusive
   isolated-capture child creation and every current source/archive270/277,
   no-skip/TUnit/browser/coverage/freshness/Pages contract. Producer worker owns
   only SiteQualificationStartupFixture.cs and SiteQualificationStartupTests.cs
   under UnitTests/Features/BenchmarkComparisons. Execute the actual source Bash
   initialization in disposable native filesystems: new/existing directories
   permit envelope redirection without replacing sentinels; file and base/parent
   symlink collisions reject before output and leave targets unchanged. Reuse
   bounded owned child-process lifetime; await child exit before cleanup. These
   unit workspace variables are filesystem inputs, never forged provider/run
   evidence. Root integrates source/build/format/Aspire tests and delivers with
   a fresh genuine entire270 run; old control actions cannot be rebound to newer
   source. Rollback removes only this scoped action/tests/gate change and
   restores the known missing-parent failure, never an alternate publication.
   Under FAIL-CANCEL, the three matrix workload/finalizer/upload stages use the
   explicit `!cancelled()` status condition, preserving execution after ordinary
   failure. Cleanup remains `always()` and allows cancelled image setup only
   through the existing run/attempt/repository/container ownership validation.
   Update existing workflow regressions before integration. Keep aggregate,
   site qualification and deploy dependency guards unchanged; cancelled jobs
   remain rejected as measurement/publication evidence. The registry test alone
   accepts legitimate final-budget timeout facts and exposes Node stderr; its
   production30s/2s bounds and success predicate remain unchanged.
5. Root reviews all diffs, builds solution, runs formatter/governance and focused
   Aspire-owned suites, then checkpoints scoped changes on current main and pushes.
6. Genuine Linux Benchmarks run qualifies all cells, aggregate, site coverage/browser
   and Pages publication. Local tests are development proof only.

Migration is additive to version4 dispositions; deploy producer/validators/site
atomically. Rollback reverts this coherent change and restores the conservative
publication gate, retaining immutable original artifacts. AC-BC-FAIL-001..011 map
to automated and actual-provider evidence in the feature specification. Root alone
owns integration and shared contract updates; workers never commit or push.

```mermaid
flowchart TD
  Runner[Aspire native runner] -->|success| Measured[Measurement envelope]
  Runner -->|failure| Unavailable[Null report envelope]
  Provider[Original GitHub job and artifact] --> Validator[Strict agreement validator]
  Measured --> Validator
  Unavailable --> Validator
  Validator --> Site[Successful values and unavailable cells]
  Site --> Gates[Full site qualification and Pages]
```
