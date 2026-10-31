# ADR-064: Four workflows and .NET publish delivery

Status: Accepted. Date: 2026-10-19.
Requirements/acceptance: REQ/AC-PIPE-001..004 and REQ/AC-REL-001..003.

Build and Tests runs the solution build and tests. Benchmarks owns isolated
measurements and authenticated aggregation. Website owns its independent
qualification and Pages publication under ADR-112.

Release has manual and reusable entries with read-only repository permissions.
Both require Build and Tests. Its manual entry uses
`dotnet publish` to build self-contained server and CLI archives for Linux, Windows
and macOS on x64 and ARM64. SDK `PublishContainer` publishes the Linux x64 server
image to `ghcr.io/managedcode/keyload` with the source SHA and moving `latest`
tags; only that job holds `packages: write`. The benchmark image stays a
downloadable Linux x64 archive tagged with the source SHA. Central source
configuration retains package and assembly versions. Actions artifacts carry the
archives and the published server image reference with its digest. Benchmarks
calls the server-image-only reusable entry and measures that exact source SHA
image, pinned to its returned digest. This entry skips application and benchmark
archives while retaining the same functional qualification gates.

The [ReleaseDelivery contract](../Features/ReleaseDelivery.md) owns the target
inventory and acceptance criteria. Release builds deployment files; runtime and
distributed database qualification remain separate. This workflow does not create
Git tags, GitHub Releases or NuGet packages; its only registry publication is the
server image.
