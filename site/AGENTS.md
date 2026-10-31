# KeyLoad site

## Purpose, entry points and ownership

- Owns the static GitHub Pages product page and its benchmark results section.
- Source: `Features/BenchmarkComparisons/` (HTML, styles, hero scene and results
  modules); entries: `scripts/results.mjs` (merge) and `scripts/build.mjs` (static
  build). The page contract is
  [`Features/BenchmarkComparisons/protocol.md`](Features/BenchmarkComparisons/protocol.md);
  the benchmark contract is
  [BenchmarkComparisons](../docs/Features/BenchmarkComparisons.md).
- `results.mjs` merges the downloaded Benchmarks result files into one sorted
  `results.json` (`results-merge.mjs`); `results-contract.mjs` owns the file shape,
  types, cases, databases and node counts. `build.mjs` validates the merged file and
  renders one comparison table per data type into static HTML at build time
  (`results-view.mjs`), then publishes the merged file unchanged as
  `data/results.json`. `results-controls.mjs` is the only browser script for the
  results: the type filter and table edge shading. Without JavaScript every table
  stays visible.
- `bootstrap.mjs` loads the hero Three.js scene: a central database, eight connected
  data models and the agent above it, with the static artwork as its fallback and
  accessible model labels. Bounds are 120,000 triangles, 30 draw calls, 1,000,000
  pixels and DPR 1.5.
- The site keeps byte-identical mirrors of the AdminDashboard `brand.css`, logo and
  icon files under the shared identity of ADR-053.

## Boundaries and protected risks

- Website (`.github/workflows/website.yml`) only downloads the result files of one
  Benchmarks run, runs the two Node scripts and deploys Pages. It has no dependency
  on product code, .NET, Aspire, AppHost, TUnit, analyzers or coverage, and product
  or test-infrastructure changes do not trigger it. Adding such coupling needs the
  owner's explicit approval.
- Show only recorded values: a failed case reads "Failed" with its error, a missing
  result reads "No data", a database without a 3-node result reads "Single node".
  Never invent, estimate or zero a missing value, and never publish test data or
  local results as measurements.
- No backend, credentials or second source of truth for results. The builder uses
  Node built-ins only; authored browser JavaScript stays within 40,960 gzip bytes
  and CSS within 20,480.
- Preserve the official Three.js 0.186.1 vendor files byte-for-byte against their
  manifest and license. `.gitattributes` excludes only upstream `space-before-tab`
  findings for those exact two files.
- Keep copy in English, accessible and free of production readiness, durability or
  winner claims. Follow the [root policy](../AGENTS.md).

## Commands and verification

From the repository root:

```sh
node site/scripts/results.mjs --input=<downloaded artifacts> --output=<new>/results.json
node site/scripts/build.mjs --results=<new>/results.json --output=<new directory> --site-revision=<40-hex revision>
node site/scripts/build.mjs --results=none --output=<new directory> --site-revision=<40-hex revision>
```

`tests/KeyLoad.WebsiteTests` runs these scripts through Node on its test result
files and checks the merged file and rendered tables. A local build or preview is
development evidence; Pages publication is proven only by a successful Website run.

## Skills

Use the relevant installed formatting or diagnostics skill when the task needs it.
Follow explicit owner scope and dependency-adoption rules; no new library or
package version is authorized by this policy.
