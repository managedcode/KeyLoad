# ADR-109: Native vector qualification and additional comparison databases

Status: Accepted by the owner's 2026-10-05 implementation request; qualification pending.

## Problem and decision

The existing 4,096-record control measures exact vectors, while the separate
100,000/1,000,000-record active lane measures document CRUD only. Neither
establishes vector scale, approximate recall or index construction cost. SurrealDB
and HelixDB are absent from the website-producing native inventory.

Add both real engines and a separate immutable vector profile family. The owner
subsequently limited every active benchmark family to 100k and 1m records. Preserve
the existing control workload and truthful historical artifacts. Do not rename
approximate search as exact, fabricate native clusters, infer database RAM from
client RSS, or publish local measurements. The acceptance contract is
[VectorQualification](../Features/BenchmarkComparisons/VectorQualification.md).

```mermaid
flowchart LR
    Contract[Closed corpus and vector profiles] --> Plan[Named database job matrices]
    Plan --> Aspire[One native topology per isolated runner]
    Aspire --> Verify[Loaded corpus and exact reference verification]
    Verify --> Build[Native index build timing]
    Build --> Queries[Measured queries and mixed updates]
    Queries --> Workers[Original worker and server resource receipts]
    Workers --> Aggregate[Authenticated complete family aggregate]
    Aggregate --> Site[Scale accuracy build cost latency and RAM]
```

## Implementation contract

Related requirements: REQ-VQ-001..007; acceptance: AC-VQ-001..007.

1. TASK-VQ-001 (lead): freeze shared profile/report/interfaces and corpus/query/
   recall contracts with focused real-flow TUnit tests before adapter integration.
2. TASK-VQ-002 (native-engine worker): native SurrealDB/HelixDB adapters under
   `benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/Targets/`; matching
   Aspire resources, immutable image/source pins, host factory registration and
   native comparison regressions. The worker owns these new engine paths and
   reports every shared join to the lead. No global tool installation.
3. TASK-VQ-003 (vector worker): corpus, exact reference, runner and PostgreSQL
   native exact/HNSW/IVFFlat execution under the same feature's Vector subdomain;
   real query/update results, index metadata and recall validation. Other native
   targets advertise only capabilities they actually implement.
4. TASK-VQ-004 (pipeline worker): canonical inventory, isolated plans, per-engine
   workflow groups, strict original-artifact aggregation and website rendering;
   TUnit plan/admission/browser regressions. Own scripts/site/workflows, without
   rewriting immutable historical evidence or manufacturing new measured cells.
5. TASK-VQ-005 (lead): inspect every diff, integrate configuration/report/image/
   resource joins, run governance, restore, build, Aspire-owned unit/comparison/
   site suites and formatter, then final canonical solution build. Recovery/RF3
   gates remain mandatory for product changes; this adapter work does not waive
   existing qualification. Commit completed stages on the current branch.
6. TASK-VQ-006 (lead): push stable changes and observe genuine Linux Benchmarks
   workers and authenticated aggregation/site delivery. Record exact SHA/run/
   attempt/original artifacts; configuration alone is not native proof.

Baseline: existing source review identifies only exact vectors in the small
control; no new native or vector qualification has run. The shared checkout has
unrelated active changes. Previous canonical build failed on an unrelated XML
parameter warning in OrleansNode; recheck the current snapshot before tests.

## Boundaries, migration and risks

This changes benchmark adapters, report/configuration contracts and publication,
not KeyLoad product storage or RF3 authority. Reuse central package pins and
Aspire resources. Native external APIs are researched from official source;
unqualified/unsupported community topologies carry an explicit unavailable
receipt. SurrealDB experimental TiKV clustering must not be advertised as a
qualified durable peer without native membership/fault/resource evidence.

New profile identifiers are additive and exact. Live validation admits only the
new producer's complete inventory and source-bound receipts. Old receipts retain
their original schemas and cohort identities as history. Rollback reverts the
new producer/consumer changes together; it cannot relabel old measurements as
new qualification. Failures, insufficient recall, unexpected plans, resource
mismatch, missing/corrupt artifacts or incomplete cleanup fail the affected cell.

Completion requires all mapped criteria and final verification, including actual
native workload artifacts. Until then this ADR remains Accepted.

## Typed native execution policy

The 2026-10-06 general configuration correction requires the bounded execution
policy join specified in VectorQualification TASK-VQ-POLICY-001. The comparison
host centrally binds and validates IOptions; adapters consume typed policy and
retain effective limits. Immutable profile/corpus/index identities stay frozen.
No unrelated product configuration or protocol migration is authorized here.

TASK-VQ-JOB-001 repairs the image preparation/current-job authority join under
AC-VQ-007 and AC-ISO-006/007. Image preparation selects the original control
cohort without a workload cell; worker selection binds the canonical cell,
job name and profile before the unchanged authenticated GitHub transport.
The exact selection, rejection and real-operation test contract is specified
in VectorQualification. The lead owns docs and final integration; the tooling
worker owns the selection helper and its focused Node/TUnit regressions.

TASK-VQ-NEST-001 repairs the nested native fixture's test-selector composition
under AC-VQ-005 and the existing Aspire-owned test boundary in ADR-074. Cleared
environment selectors are unselected; explicit empty CLI selections and
conflicting native workloads remain rejected. Its exact scope, reproduced
baseline and real composition/native-flow checks are in VectorQualification.

TASK-VQ-CLOCK-001 joins the native target/session and vector factory provider
propagation under AC-VQ-005/006 and the owning clock contract in ADR-113. The
worker owns the bounded propagation and native flow observations; the lead
owns integration, canonical build/format, actual Aspire flows and exact-source
Linux qualification. Preserve per-instance validated vector options and actual
native state, data, cancellation and index contracts. VectorQualification
defines the exact provider observations and preserved-state negative flow.
