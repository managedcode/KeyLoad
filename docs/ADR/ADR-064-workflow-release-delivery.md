# ADR-064: Four workflows and .NET publish delivery

Status: Accepted. Date: 2026-10-19.
Requirements/acceptance: REQ/AC-PIPE-001..004 and REQ/AC-REL-001..003.

Build and Tests runs the solution build and tests. Benchmarks owns isolated
measurements and authenticated aggregation. Website owns its independent
qualification and Pages publication under ADR-112.

Release is a manual, read-only build workflow. It uses `dotnet publish` to build
self-contained server and CLI archives for Linux, Windows and macOS on x64 and
ARM64. SDK `PublishContainer` builds downloadable Linux x64 server and benchmark
image archives. The source SHA identifies image tags; central source configuration
retains package and assembly versions. Actions artifacts carry the outputs.

The [ReleaseDelivery contract](../Features/ReleaseDelivery.md) owns the target
inventory and acceptance criteria. Release builds deployment files; runtime and
distributed database qualification remain separate. This workflow does not create
Git tags, GitHub Releases, NuGet packages or registry publications.
