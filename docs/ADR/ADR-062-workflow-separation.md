# ADR-062: Workflow responsibility and qualification boundaries

Status: Accepted; complete delivered-source qualification pending.
Features: RepositoryGovernance, BenchmarkComparisons.
Related: [ADR-064](ADR-064-workflow-release-delivery.md),
[ADR-112](ADR-112-independent-website-publication.md).

## Decision

Use exactly four workflows. Build and Tests owns full solution restore/build,
format, analyzers, governance, unit/scalar, process recovery and genuine cluster.
Benchmarks owns every performance/comparison workload, images and isolated
Linux cells, original per-database results and one authenticated aggregation.
Website independently qualifies and publishes current source with optional
verified benchmark data. Release prepares source-bound versioned distributions;
publication waits for product readiness or a direct owner release instruction.

Every comparison database has its own readable job group and complete source-bound
matrix; each cell runs alone with that engine's actual topology and loader.
One- and two-node benchmark profiles never replace production cluster. Preserve the
complete canonical planned inventory, exact scale/ACK/correctness/resource contracts,
all image gates and every original result, including failures and unavailable cells.

```mermaid
flowchart LR
    Source[Current source] --> Tests[Build and Tests]
    Source --> Benchmarks[Isolated database job groups]
    Benchmarks --> Aggregate[Authenticated complete accounting]
    Aggregate --> Trigger[Final Benchmarks dispatch]
    Trigger --> Website[Independent Website qualify and deploy]
    Source --> Website
    Tests --> Release[Owner admitted source bound Release]
```

## Implementation and acceptance

1. Shared workflow identities, file ownership and REQ/AC are frozen before edits.
2. Disjoint workflow/composite changes are owned by infrastructure; product code preserves actual callers, image receipts and bounded shutdown.
3. Join exact workflow names/paths/events with source/run/attempt/job/artifact
   validators, release context, source closure and all SDK/MCP test entry points.
   Never relabel originals, accept a different source cohort or bypass a failed gate.
4. Validate YAML/action contracts statically; source-shape assertions are infrastructure
   review. Actual admission, wrong executor, corrupted artifact and healthy
   follow-up operations run through the TUnit entry.
5. Combined source, build/format/governance and original Linux runs are reviewed.
   The review covers responsibilities and actual distribution artifacts, execution
   provenance, Website qualification with fresh source/producer identities and
   complete traceable delivery.

Rollout is one source checkpoint containing workflows, validators, actual callers,
source maps and docs. Source rollback keeps the four mandatory responsibilities
and every required gate. Database bytes, credentials, replication and public operation
semantics are unchanged. Original evidence remains immutable; missing, failed,
skipped or mixed-source evidence cannot become current measurements or qualification.
