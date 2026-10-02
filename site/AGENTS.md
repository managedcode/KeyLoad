# KeyLoad site

## Purpose and entry points
- Owns the static Pages website that presents benchmark comparisons using verified GitHub Actions reports.
- HTML entry: `index.html`; behavior and rendering: `app.js`, `styles.css`; report validation: `measurements.mjs`; site builder: `scripts/build.mjs`; Node tests: `scripts/measurements.test.mjs`.

## Ownership and boundaries
- Feature-owned site behavior belongs under `Features/<SliceName>/` with the same canonical feature name and `docs/Features/<SliceName>.md`; current root-level site files are migration debt.
- The site displays evidence; it does not generate or qualify benchmark results. Pages workflow must select successful CI evidence and preserve source revision/run links. No fabricated, local-only, durability or production-readiness claims.
- Do not introduce a backend, credentials, or a second source of truth for benchmark measurements.
- Preserve the official Three.js 0.186.1 vendor files byte-for-byte against their local manifest and license. `.gitattributes` excludes only upstream `space-before-tab` findings for those exact two files so generic diff checks do not require mutating vendor bytes; authored site files retain normal whitespace checks.

## Commands and evidence
- GitHub Pages workflow runs `node --test site/scripts/measurements.test.mjs`, then `node site/scripts/build.mjs --reports=artifacts/comparisons --output=_site --revision="$EVIDENCE_REVISION" --evidence-url="https://github.com/$GH_REPO/actions/runs/$EVIDENCE_RUN"` after downloading the `comparison-suite` artifact.
- Run site validation only through `.github/workflows/pages.yml`; do not claim a local run qualifies published evidence.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve accessible rendering, provenance links and strict input validation. Never include credentials or claim site publication until the Pages workflow succeeds.

## Read-first and canonical slice ownership
- Read the [root policy](../AGENTS.md), [architecture map](../docs/Architecture.md), [RepositoryGovernance feature](../docs/Features/RepositoryGovernance.md), and [ADR-032](../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `BenchmarkComparisons`; target path: `Features/BenchmarkComparisons/`, matching `docs/Features/BenchmarkComparisons.md`.
- The current Pages workflow uses Node’s `node --test` runner; this conflicts with root policy requiring TUnit for all tests and is migration debt, not permission to add another framework.

## ADR-040 migration execution
- The old entry/command listings above describe the pre-migration state. Preserve these rules and their provenance; the stricter root TUnit requirement controls execution. The replacement canonical HTML/modules/styles are `Features/BenchmarkComparisons/`, with `scripts/build.mjs` as the thin build entry and `tests/KeyLoad.SiteTests` as the only qualification runner. Remove the superseded flat assets and Node test only after all replacement packets are joined and inspected.
- Read `site-design.acceptance.md`, `site-design.plan.md`, ADR-040 and the frozen feature `protocol.md`. Site validation remains in `pages.yml`; its explicit validation mode must not deploy. Successful source/run verification precedes report download; website and measured revisions remain separate. No DNS/publication in the design task.
- A local isolated static build and real-browser design/graphics inspection are the narrowly specified ADR-040 manual exceptions. They cannot qualify numeric tests, database behavior, coverage, publication or durability.
