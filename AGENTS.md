# KeyLoad

Implement the architecture in `docs/design/architecture-v0.3.uk.md`. Treat it as the product specification; user instructions control scope and authorization.

Work in this checkout. Preserve unrelated changes. The first server topology is an RF3 cluster; do not replace it with an in-memory or single-node demo. Keep the atomic partition distinct from physical placement. A node-local PartitionHost owns storage, journals, file locks and the apply gate; Orleans grains route commands.

Use .NET 10, centrally pinned packages, and lock files. Required checks: build the solution, unit tests, real process recovery tests, and Aspire RF3 integration tests. Keep `docs/implementation/status.json` and the README honest about qualification gates. Do not claim power-loss durability from process-kill tests or production readiness without the endurance and fault gates.

ManagedCode packages are our projects. Fix dependency defects in their owning sibling repository, with regression tests, canonical patch release, successful GitHub publication and verified NuGet availability before updating KeyLoad. Preserve unrelated work; never force-push or bypass repository protections. The user authorizes scoped dependency repair commits, pushes and releases, and stable KeyLoad commits and pushes to main.
