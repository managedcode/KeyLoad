# IsolatedCellSetup

## Purpose and entry points
- Own the composite native setup for one BenchmarkComparisons cell; entry action.yml.

## Boundaries
- Read root AGENTS.md, workflows/AGENTS.md and ADR056 before changes.
- One Linux job owns one engine/node/scenario. This action only authenticates own job and imports the shared source-bound image bundle; it does not run measurements or qualify a cohort.
- Preserve exact native image/source/hash checks, real Docker, least privileges, bounded setup and secret-free output. No alternate run, rebuild, test double or local qualification.

## Commands and verification
- Execute only through ci.yml exact-SHA GitHub cells; root unit/tooling/native270 gates are mandatory.
- Static governance and YAML/source review are source validation only.

## Skills and risks
- No local skills installed and no installation permitted. Root applies available Orleans/Aspire guidance when relevant.
- Shared workflow contracts and image/provider boundaries have one root integration owner.
