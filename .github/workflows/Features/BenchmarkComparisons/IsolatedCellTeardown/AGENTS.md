# IsolatedCellTeardown

## Purpose and entry points
- Own bounded cell image-registry cleanup and native evidence retention; entry action.yml.

## Boundaries
- Read root AGENTS.md, workflows/AGENTS.md and ADR056. One caller job owns one native engine/node/scenario.
- Only source-owned registry cleanup is permitted. Always retain metadata and compiler/native results. This action never qualifies measurements or repairs failed raw evidence.
- Keep real Docker, exact image ownership, least privileges and secret-free logs. No local tests, fake results or alternate source.

## Commands and verification
- Run only through exact-SHA ci.yml GitHub cells. Static governance is not native qualification.

## Skills and risks
- No local skills installed or installation permitted.
- Root owns shared infrastructure joins; failed cleanup makes the native job fail and cannot publish.

## Owner-directed comparison pipeline, 2026-10-03
- ADR-062 moves the exact-SHA native comparison cells from the historical ci.yml placement above to benchmarks.yml (`Benchmarks`). Every existing cleanup, retention, isolation and publication-failure requirement remains mandatory.

## Owner-directed failed-cell publication, 2026-10-04
- Retain original failed runner reports separately from the null-report worker envelope, plus the finalization job capture. Workload failure may be published as unavailable under ADR-080; teardown failure itself remains a failed ownership/cleanup gate and cannot validate a measured cell.
