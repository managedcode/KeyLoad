# KeyLoad.Server

## Purpose and entry points
- Owns the KeyLoad server process, HTTP API composition and Orleans silo/node configuration.
- Entry point: `Program.cs`; API routes: `Features/ClientApi/Transport/ApiEndpoints.cs`; Orleans node setup: `Features/ClusterRouting/Hosting/OrleansNode.cs`; configuration: `NodeOptions.cs`.

## Ownership and boundaries
- Server routes public requests to database/grain behavior. New endpoint behavior belongs under `Features/<SliceName>/` and matching `docs/Features/<SliceName>.md`; keep the process entry point as composition.
- Orleans is the cluster foundation. A separate request grain invokes required database grains; node-local `PartitionHost` owns storage, journals, file locks and apply gate. Atomic partition identity remains distinct from physical placement.
- Existing root-level endpoints and host-process RF3 topology are migration debt. Do not accept client-supplied trusted roles or replace Docker/Aspire RF3 with an in-memory/single-node demo. Do not expand DotNext cluster use.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Owner correction2026-10-03 requires public service verification through the actual Aspire caller: `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=rf3`. Direct native `dotnet test` is only the AppHost-owned child, never an alternative caller entry. Local development is authorized; exact-source Linux GitHub qualification remains mandatory.

## Skills and protected risks
- Owner-authorized applicable skill: Orleans 3.1.1 at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`; apply it to silo startup, provider configuration, directory/migration and shutdown. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Authentication, authorization, endpoint limits, cancellation and health/readiness are protected. Never log secrets or claim deployment/production qualification from a build alone.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Shared API transport, identity and routing follow `ClientApi`, `Authorization` and `ClusterRouting` under their matching `Features/` paths. Business endpoint behavior MUST mirror its owning business slice under `Features/<same-business-SliceName>/`, including `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution` and `ChangeFeeds`.
- `Program.cs` is the shared composition root; `Features/ClientApi/Transport/ApiEndpoints.cs`, `Features/ClusterRouting/Hosting/OrleansNode.cs`, and `NodeOptions.cs` are current entry points, not separate feature names.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
