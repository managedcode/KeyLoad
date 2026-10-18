# ADR-109: Native vector qualification and additional comparison databases

Status: Accepted; qualification pending.

## Problem and decision

The existing 4,096-record control measures exact vectors, while the separate
100,000/1,000,000-record active lane measures document CRUD only. Neither
establishes vector scale, approximate recall or index construction cost. SurrealDB
and HelixDB are absent from the website-producing native inventory.

Add both real engines and a separate immutable vector profile family. Every active benchmark family is limited to 100k and 1m records. Preserve
the existing control workload. Do not rename
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

1. Shared profile/report/interfaces and corpus/query/recall contracts are frozen with focused real-flow TUnit tests before adapter integration.
2. Native SurrealDB/HelixDB adapters live under `benchmarks/KeyLoad.Comparisons/Targets/` with matching Aspire resources, immutable image/source pins, host factory registration and native comparison regressions. No global tool installation.
3. Corpus, exact reference, runner and PostgreSQL native exact/HNSW/IVFFlat execution live under the same feature's Vector subdomain, with real query/update results, index metadata and recall validation. Other native targets advertise only capabilities they actually implement.
4. Canonical inventory, isolated plans, per-engine workflow groups, strict original-artifact aggregation and website rendering carry TUnit plan/admission/browser regressions across scripts/site/workflows, without rewriting immutable historical evidence or manufacturing new measured cells.
5. Integrate configuration/report/image/resource joins, then run governance, restore, build, Aspire-owned unit/comparison/site suites and formatter, and a final canonical solution build. Recovery and cluster gates remain mandatory for product changes; this adapter work does not waive existing qualification.
6. Push stable changes and observe genuine Linux Benchmarks workers and authenticated aggregation/site delivery, recording exact SHA/run/attempt/original artifacts; configuration alone is not native proof.

Baseline: existing source review identifies only exact vectors in the small
control; no new native or vector qualification has run. The shared checkout has
unrelated active changes. Previous canonical build failed on an unrelated XML
parameter warning in OrleansNode; recheck the current snapshot before tests.

## Boundaries, rollout and risks

This changes benchmark adapters, report/configuration contracts and publication,
not KeyLoad product storage or required-copy acknowledgementnowledged authority. Reuse central package pins and
Aspire resources. Native external APIs are researched from official source;
unqualified/unsupported community topologies carry an explicit unavailable
receipt. SurrealDB experimental TiKV clustering must not be advertised as a
qualified durable peer without native membership/fault/resource evidence.

New profile identifiers are additive and exact. Live validation admits only the
new producer's complete inventory and source-bound receipts. Each immutable source/run receipt remains bound to its original cohort and is
not treated as current scale output. Rollback reverts the
new producer/consumer changes together; it cannot relabel old measurements as
new qualification. Failures, insufficient recall, unexpected plans, resource
mismatch, missing/corrupt artifacts or incomplete cleanup fail the affected cell.

Completion requires all mapped criteria and final verification, including actual
native workload artifacts. Until then this ADR remains Accepted.

## Typed native execution policy

The 2026-10-06 general configuration correction requires the bounded execution
policy join specified in VectorQualification. The comparison
host centrally binds and validates IOptions; adapters consume typed policy and
retain effective limits. Immutable profile/corpus/index identities stay frozen.
No unrelated product configuration or protocol change is authorized here.
