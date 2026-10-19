# ReleaseDelivery tooling

## Purpose, entry points and ownership

- Owns bounded release version reservation, authenticated read-only GitHub context and asset verification helpers under the ReleaseDelivery slice. Contract: docs/Features/ReleaseDelivery.md and ADR-064; root alone owns release.yml, central source version, Dockerfiles and delivery.
- Use Node/Python standard libraries and installed GitHub runner tooling; no new dependencies or global tools/skills.
- Version logic reads canonical source major/minor, authenticated run/tag inventory and owned immutable reservation. Preserve UTC date, positive daily sequence, exact source/run/repository identity and immutable retry behavior.
- Asset validation must inspect actual package versions, database/image files, hashes and source labels; do not fabricate provider digests, CI success or database qualification.
- GitHub context reads actual authenticated REST metadata through bounded gh calls only in Release; never forge runner identity or substitute static data for CI success.
- No network mutations, tag/release/image overwrite, publication credentials, local tests, untracked user data or unrelated repository changes from these helpers.
- No applicable installed skills for this bounded release metadata tooling; install none.
- Functions <=64 lines, files <=400 lines, bounded inputs/output, explicit error cases; no consumer-side dependency workarounds.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
