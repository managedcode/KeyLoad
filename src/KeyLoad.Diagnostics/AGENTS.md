# KeyLoad.Diagnostics

## Purpose and entry points
- Owns generic bounded callback-free database phase diagnostics in `Features/ResourceExecution/`, under ADR-063 and its feature contract.
- Entry points: immutable phase/outcome/quality/snapshot schema, DatabasePhaseBank and root-owned process startup facade.

## Ownership and boundaries
- BCL-only shared implementation; no ASP.NET/Orleans/provider/product/package dependency except the existing centrally attached analyzer.
- Keep feature code in ResourceExecution and root-owned assembly friend metadata outside that slice only when solution-wide.
- Preserve both authorized cuts, physical node ownership, all existing gates/async originals/ACK/WAL. This project owns no database state, file, credential, role or protocol.
- Fixed32/6/16 schema/four stripes, startup bank128KiB, record maximum4 CAS attempts and sticky degradation; no hot callbacks, logging/export, allocations, gates, ambient state, per-request identifiers, reset or live mode changes.
- Only root freezes public shared signatures/project references and mode/export composition; workers may not invent broader telemetry/control architecture.

## Commands and evidence
- Root may perform enabled Release development build, required formatter and static governance; these are not runtime qualification.
- TUnit/MTP unit/scalar, genuine process recovery and Docker/Aspire RF3 SDK/official MCP plus profile tests run only in GitHub Actions through ci.yml (CI), which combines build and every project test; Benchmarks owns load/comparison and downstream website qualification/publication.
- Preserve exact source/run/job/artifact and actual phase/resource scopes. No local tests, allocations qualification, containers or load benchmarks.

## Skills and protected risks
- No project-local skill installed; do not install tools/skills or modify global configuration. Installed authorized Orleans guidance applies to unchanged producer lifecycles outside this BCL project.
- Unknown/partial/degraded measurements are unavailable evidence, never zero or a performance winner. Snapshot reads are non-atomic; overlapping phases and whole-process samples are not request attribution.
- Preserve root400/type200/function50/nesting3 limits, compiler/style diagnostics and all independent ownership. Stop on package/public/wire/security change or scope conflict.
