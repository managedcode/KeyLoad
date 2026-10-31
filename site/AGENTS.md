# KeyLoad site

## Purpose, entry points and ownership

- Owns the static Pages website that presents benchmark comparisons using verified GitHub Actions reports.
- HTML/modules/styles entry: `Features/BenchmarkComparisons/`; static builder: `scripts/build.mjs`. Website only downloads the latest successful original benchmark data, generates the landing page and publishes Pages. It has no dependency on product code, .NET, Aspire, AppHost, TUnit, Chrome suites, analyzers or coverage. Never introduce those as Website steps or publication prerequisites without the owner's explicit approval. Product/test-infrastructure changes do not trigger this workflow.
- The site displays the latest completed successful full Benchmarks run, retaining its original source, attempt, image digests and declared workload inventory. New failed or incomplete runs never replace existing figures with an empty state. Require the complete planned worker inventory and original TRX, runtime, resource, correctness and artifact proof. Completed operation measurements may publish when server resource telemetry is incomplete, with an explicit incomplete-telemetry label and the original resourceQualified=false preserved. Display recorded metrics only; capabilities outside the original workload have null metrics displayed as "No data". Every category uses the same comparison table, available through horizontal scrolling on mobile. Preserve every execution, correctness, provenance and publication gate. Website recorded-operation display does not claim identical runner hardware; native comparison aggregation retains its separate strict resource-comparability gate. No fabricated, local-only, durability or production-readiness claims.
- Do not introduce a backend, credentials, or a second source of truth for benchmark measurements.
- Preserve the official Three.js 0.186.1 vendor files byte-for-byte against their local manifest and license. `.gitattributes` excludes only upstream `space-before-tab` findings for those exact two files so generic diff checks do not require mutating vendor bytes; authored site files retain normal whitespace checks.
- Use the installed Orleans skill for Orleans decisions and the relevant installed skill for formatting, Aspire or diagnostics. Follow the owner's explicit installation and dependency scope.
- Preserve accessible rendering, provenance links and strict input validation. Never include credentials or claim site publication until the Pages workflow succeeds.

The primary hero is the existing animated Three.js composition with a central database, eight connected data models and the agent above it. Preserve the artwork fallback and accessible model labels. Bounds are 120,000 triangles, 30 draw calls, 1,000,000 pixels and DPR 1.5. Preserve official vendor files, manifests and licenses byte-for-byte. Product content may publish without benchmark figures; displayed figures require authenticated original GitHub artifacts.

## Boundaries and protected risks

Follow the [root policy](../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node site/scripts/build.mjs
```

For this static site, verify JavaScript/YAML syntax and the actual static build. Product solution builds and test gates belong to their independent workflows. Public measurements still require original successful GitHub Benchmarks artifacts.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
