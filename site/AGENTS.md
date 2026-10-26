# KeyLoad site

## Purpose, entry points and ownership

- Owns the static Pages website that presents benchmark comparisons using verified GitHub Actions reports.
- HTML/modules/styles entry: `Features/BenchmarkComparisons/`; thin builder: `scripts/build.mjs`; real qualification: `tests/KeyLoad.WebsiteTests` through the unified Aspire AppHost.
- The site displays verified successful sessions from one authenticated completed Benchmarks run/attempt/source, including a run whose overall conclusion is failure. Preserve the complete planned row inventory; failed or missing sessions have null metrics displayed as an em dash. Every included session retains its complete original TRX, runtime, resource, correctness and artifact proof. Authentic sessions with incomplete resource evidence may appear as metadata, with null metrics and an em dash; each displayed numeric value still requires resourceQualified=true and every existing qualification gate. No fabricated, local-only, durability or production-readiness claims.
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

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
