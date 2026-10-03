# BuildIsolatedSite

## Purpose and entry points
- Own the final BenchmarkComparisons website build composite action; entry point: action.yml.
- Read the root AGENTS.md, workflows/AGENTS.md, docs/Architecture.md, BenchmarkComparisons feature, isolated-comparisons.acceptance.md TASK-ISO-012P and ADR-056 before changes.

## Boundaries
- Run from the actual qualified website checkout after all native site/analyzer TUnit, coverage and no-skip gates pass. Keep website, historical measured, isolated measured and workflow-control revisions independent.
- Preserve the legacy twelve-file archive checks, three historical profiles, raw-byte comparisons, actual current qualification-job lookup and existing publication schema fields intact.
- Add only the approved isolated cohort: verify original archives and all277 input hashes before and after the bounded final builder, retain the exact aggregate manifest and copy the bounded original archive receipt under publication.isolated.
- Root alone owns pages.yml, permissions, deployment, shared hooks, environment, contracts, inventories and source/coverage joins. This action neither authenticates a different executor nor grants deployment permission.

## Commands and verification
- Execute only inside the exact-source pages.yml qualification job; native build and input verification are bounded to300 seconds.
- Static YAML, shell syntax, provenance/source and whitespace review are source checks only. Required genuine GitHub TUnit, browser, coverage, same-archive and deployment-freshness gates remain mandatory.
- Do not run local tests, builds, providers, benchmarks or publication commands.

## Skills and protected risks
- No applicable workflow skill is installed; installing skills or tools is prohibited.
- Preserve least privilege and the actual inherited GitHub context. Never log secrets, forge a CI environment, add a provider fallback, weaken gates or rewrite measured inputs.
- Require the isolated receipt to be a regular non-link file of at most4MiB before parsing; preserve original receipt bytes and hashes across the builder. Do not use generic archive extraction or replace original authority with a rewritten receipt.
