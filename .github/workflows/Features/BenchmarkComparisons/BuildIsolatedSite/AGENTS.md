# BuildIsolatedSite

## Purpose, entry points and ownership

- Own the final BenchmarkComparisons website build composite action; entry point: action.yml.
- Read the root AGENTS.md, workflows/AGENTS.md, docs/Architecture.md, BenchmarkComparisons feature acceptance/execution contracts and ADR-056 before changes.
- Run only the static Node builder from the website checkout. No .NET, Aspire, AppHost, containers, TUnit/browser/analyzer suites, coverage or product qualification may block this action or Pages publication. Adding such coupling requires specific owner approval. Keep website, measured and workflow-control revisions accurate and independent.
- Original successful session artifacts from one authenticated completed run are the sole measurement authority. Preserve raw-byte comparisons, actual build-job lookup, source identities and the original isolated receipt. Publication records the static build and original data; it must not claim product or test qualification.
- Verify the selected original worker/runtime archives and every receipt-derived input hash before and after the bounded standalone builder, retain the exact aggregate manifest and preserve the bounded original archive receipt under publication.isolated.
- Preserve least privilege and the actual inherited GitHub context. Never log secrets, forge a CI environment, add a provider fallback, weaken gates or rewrite measured inputs.
- Require the isolated receipt to be a regular non-link file of at most4MiB before parsing; preserve original receipt bytes and hashes across the builder. Do not use generic archive extraction or replace original authority with a rewritten receipt.

## Boundaries and protected risks

Follow the [root policy](../../../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node --check site/scripts/build.mjs
```

For this static publication action, use YAML/JavaScript syntax checks and the actual Node build. Product builds and test qualification are independent. Public figures retain original successful GitHub Benchmarks evidence.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
