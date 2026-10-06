# ReleaseDelivery tooling

## Purpose and entry points
- Owns bounded release version reservation, authenticated read-only GitHub context and asset verification helpers under the ReleaseDelivery slice. Contract: docs/Features/ReleaseDelivery.md and ADR-064; root alone owns release.yml, central source version, Dockerfiles and delivery.

## Boundaries
- Use Node/Python standard libraries and installed GitHub runner tooling; no new dependencies or global tools/skills.
- Version logic reads canonical source major/minor, authenticated run/tag inventory and owned immutable reservation. Preserve UTC date, positive daily sequence, exact source/run/repository identity and immutable retry behavior.
- Asset validation must inspect actual package versions, database/image files, hashes and source labels; do not fabricate provider digests, CI success or database qualification.
- GitHub context reads actual authenticated REST metadata through bounded gh calls only in Release; never forge runner identity or substitute static data for CI success.
- No network mutations, tag/release/image overwrite, publication credentials, local tests, untracked user data or unrelated repository changes from these helpers.

## Commands and verification
- Static syntax: node --check release-version.mjs and CLI modules; Python AST parsing for Python helpers without execution.
- Real production helper regressions execute through TUnit only in GitHub CI. Exact-source Release build/artifact/provider inspection belongs to the lead/workflow; static checks are not delivery.

## Skills and protected risks
- No applicable installed skills for this bounded release metadata tooling; install none.
- Functions <=64 lines, files <=400 lines, bounded inputs/output, explicit error cases; no consumer-side dependency workarounds.
