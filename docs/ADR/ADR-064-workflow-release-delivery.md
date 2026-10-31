# ADR-064: Four workflows and .NET publish delivery

Status: Accepted. Date: 2026-10-19.
Requirements/acceptance: REQ/AC-PIPE-001..004 and REQ/AC-REL-001..003.

Build and Tests runs the solution build and tests. Benchmarks owns one
measurement job per database, data type and node count under
[ADR-150](ADR-150-per-type-benchmark-jobs.md). Website builds the static site from
one Benchmarks run's result files and publishes Pages.

Release is a manual workflow with read-only repository permissions. It uses
`dotnet publish` to build self-contained server and CLI archives for Linux, Windows
and macOS on x64 and ARM64. SDK `PublishContainer` publishes the Linux x64 server
image to `ghcr.io/managedcode/keyload` with the source SHA and moving `latest`
tags; only that job holds `packages: write`. Central source
configuration retains package and assembly versions. Actions artifacts carry the
archives and the published server image reference with its digest. Benchmarks
measures the server image that `latest` resolves to, pinned by digest for the run.

The [ReleaseDelivery contract](../Features/ReleaseDelivery.md) owns the target
inventory and acceptance criteria. Release builds deployment files; runtime and
distributed database qualification remain separate. This workflow does not create
Git tags, GitHub Releases or NuGet packages; its only registry publication is the
server image.
