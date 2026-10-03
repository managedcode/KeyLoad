# ResourceExecution in KeyLoad.Orleans

## Purpose and entry points
- Own the internal immutable cache-control metadata and bounded validation, canonical transcript, authentication and correlation primitives accepted by ADR-058 R82.
- Unused internal source entry points: CacheControlWire, CacheControlAuthenticator and CacheControlCorrelation; native qualification remains pending. The exact types/aliases/Ids/APIs are in docs/Features/ResourceExecution/CacheControlV1.md.
- Read the solution and Orleans project AGENTS.md, docs/Architecture.md, ResourceExecution feature and ADR-058 before changing this slice.

## Boundaries and ownership
- Root owns shared generated metadata, enums, marker interfaces, primitives, docs/config and final integration. Workers must stay within explicit disjoint file scopes and escalate ambiguity.
- R82 adds unused internal primitives only. Keep receiver/coordinator/replay/timers/discovery/server DI/native probes and RF3 cache admission outside this approved stage.
- Preserve node-local physical store ownership, one-grain-per-data-request, current public authorization/read barriers and every existing database/replica/token/persisted byte contract.
- Never apply the separately held native internal-format migration as a prerequisite or workaround.
- Validate the complete closed shape and all complete/signing/correlation lengths before transcript allocation. Authenticate nested proofs and exact request correlation; a signed tuple never creates a lease or Byzantine voter authority.
- Private signing material never enters metadata, diagnostics or tests' output. Final disposal closes and zeroes it; original concurrent tasks must finish or remain finitely observed before owners are released.
- A failed finite wait must retain a completion owner for unsettled originals; signing and coordination resources are released only after actual originals settle, with complete fault observation and no late mutation of reported failures.
- Inherit file400/type200/function50/nesting3 limits and all stricter root quality policies. No analyzer suppression or weakened assertions.

## Commands and evidence
- Development source build: dotnet build KeyLoad.slnx --no-restore --configuration Release; formatter: dotnet format KeyLoad.slnx --verify-no-changes --no-restore; governance: node scripts/Features/RepositoryGovernance/verify.mjs.
- Test execution is GitHub Actions only. Native UnitTests normal/scalar use actual pinned Orleans Serializer/DI and real .NET crypto; required recovery and Docker/Aspire RF3 run through the actual SDK/MCP clients.
- Do not run local tests, crypto qualifications, benchmarks or test images. A local build, deterministic source vector or contract review does not qualify runtime, coverage, performance or power loss.

## Skills and protected risks
- Apply the already owner-authorized Orleans skill at /Users/ksemenenko/.codex/skills/orleans/SKILL.md. Install no other skills/tools or global configuration.
- Protect exact versioned aliases/Ids, dedicated purposes, intrinsic32-byte digest/RFC4122 Guid encoding, flat-rejection shapes, immutable inputs, complete preflight and constant-time comparisons.
- Controller and image-only probe implementation requires its own accepted future lifecycle/discovery/migration contract; no public debug surface or fourth silo.
