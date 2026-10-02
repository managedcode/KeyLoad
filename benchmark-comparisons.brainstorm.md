# BenchmarkComparisons expansion

The owner wants comparable real Docker/Aspire workloads across KeyLoad, PostgreSQL/pgvector, Qdrant, RabbitMQ, Redis, Neo4j Community, MongoDB Community, OpenSearch and KurrentDB, including multi-node variants. Published README/site charts must use only JSON from successful GitHub Actions runs.

## Options and direction

- Independent single-node engines remain useful as a clearly labeled baseline, while KeyLoad always retains its real RF3 group.
- Add native three-data-copy replicated profiles with observed membership and explicit acknowledgement/read guarantees. Three independent standalone containers are not a cluster.
- Neo4j Community has no native cluster under the allowed feature/license scope, so its replicated cases remain unsupported with an explicit reason. Do not activate Enterprise.
- Add one-event append/read scenarios on engines with a real stream or transactional record implementation. Validate event identity, normalized revision and full JSON.
- Keep corpus, parameters, engine versions, image digests, errors, raw samples and source revision in every report. Do not weaken correctness or acknowledgements to improve rankings.
- Publish immutable CI JSON and automatically derived SVG charts to GitHub; README and Pages link to raw evidence. Never paste measured values into source.

## Parallel ownership and constraints

External adapters and evidence/chart tooling can be implemented independently. Root owns contracts, target registration, dataset/runner integration, shared configuration and docs. Aspire resource changes must wait for the concurrent Orleans/Docker migration owner or be confined to feature-specific external-resource helpers with no overlap in the shared AppHost composition root. Public KeyLoad client contracts are preserved.

No local test or benchmark runs are permitted. Qualification occurs in GitHub Actions at a complete committed source snapshot. Existing mixed-framework/Node-runner, consensus and host-process migrations remain mandatory gaps; this feature cannot claim they are qualified by an older run.

## Performance work

Find bottlenecks using qualified CI reports. Compare optimization before/after only with matching parameters and topology. Graph traversal currently reloads shared reachable vertices; per-read-cut authorization/visibility reuse is a candidate, but concurrent Core changes must be joined before editing it. A universal performance victory is not an acceptance condition; report observed wins/losses by supported workload and guarantees.
