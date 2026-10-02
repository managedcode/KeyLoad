# KeyLoad.Server

## Purpose and entry points
- Owns the KeyLoad server process, HTTP API composition and Orleans silo/node configuration.
- Entry point: `Program.cs`; API routes: `ApiEndpoints.cs`; Orleans node setup: `OrleansNode.cs`; configuration: `NodeOptions.cs`.

## Ownership and boundaries
- Server routes public requests to database/grain behavior. New endpoint behavior belongs under `Features/<SliceName>/` and matching `docs/Features/<SliceName>.md`; keep the process entry point as composition.
- Orleans is the cluster foundation. A separate request grain invokes required database grains; node-local `PartitionHost` owns storage, journals, file locks and apply gate. Atomic partition identity remains distinct from physical placement.
- Existing root-level endpoints and host-process RF3 topology are migration debt. Do not accept client-supplied trusted roles or replace Docker/Aspire RF3 with an in-memory/single-node demo. Do not expand DotNext cluster use.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Public service behavior is exercised through IntegrationTests: `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release`, dispatched only by `.github/workflows/ci.yml`.

## Skills and protected risks
- Owner-authorized applicable skill: Orleans 3.1.1 at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`; apply it to silo startup, provider configuration, directory/migration and shutdown. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Authentication, authorization, endpoint limits, cancellation and health/readiness are protected. Never log secrets or claim deployment/production qualification from a build alone.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Shared API transport, identity and routing follow `ClientApi`, `Authorization` and `ClusterRouting` under their matching `Features/` paths. Business endpoint behavior MUST mirror its owning business slice under `Features/<same-business-SliceName>/`, including `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution` and `ChangeFeeds`.
- `Program.cs` is the shared composition root; `ApiEndpoints.cs`, `OrleansNode.cs`, and `NodeOptions.cs` are current entry points, not separate feature names.
