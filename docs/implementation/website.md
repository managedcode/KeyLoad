# Public benchmark website

The static product page with the benchmark results lives in `site/`. GitHub Pages serves it at `https://www.keyload.cloud/`. The apex `keyload.cloud` redirects to the canonical `www` address once GitHub Pages has accepted the DNS records and issued its certificate.

The domain uses GitHub's four apex A records (`185.199.108.153`, `.109.153`, `.110.153`, `.111.153`) and `www CNAME managedcode.github.io`. Existing mail, verification and Azure service records do not belong to this website. GitHub Pages uses Actions as its build source. The generated artifact includes `CNAME` with `www.keyload.cloud`; the repository Pages setting owns the custom domain for an Actions deployment. Follow the [GitHub custom-domain documentation](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site) for certificate and redirect behavior.

## Publication

The [Website workflow](../../.github/workflows/website.yml) builds the site from the
result files of one Benchmarks run and deploys GitHub Pages. Benchmarks dispatches
it with its run id after its measure jobs finish; a main push that changes `site/**`
or the workflow, and a manual dispatch without a run id, use the latest completed
Benchmarks run on `main`. Build and Tests ignores pushes that change only these
inputs, and Website never builds .NET, starts Aspire or runs tests.

1. `gh run download` fetches the run's `benchmark-*` artifacts.
2. `node site/scripts/results.mjs --input=results --output=site-results/results.json`
   merges every result file into one sorted `results.json`. It fails when the run
   has no result file, when a file is malformed or does not match its own name, or
   when two files describe the same database, type and node count; the workflow
   then stops and the current Pages stay.
3. `node site/scripts/build.mjs --results=site-results/results.json --output=_site
   --site-revision=<sha>` renders one comparison table per data type into static
   HTML at build time and publishes the merged file as `data/results.json`.
4. The Pages jobs upload `_site` and deploy it.

The HTML, styles and result modules live in `site/Features/BenchmarkComparisons/`
(`results-contract.mjs`, `results-merge.mjs`, `results-view.mjs`);
`results-controls.mjs` is the only browser script for the results (type filter and
table edge shading). The [site protocol](../../site/Features/BenchmarkComparisons/protocol.md)
owns the file shape and cell rendering, and
[BenchmarkComparisons](../Features/BenchmarkComparisons.md) owns the benchmark
contract. `tests/KeyLoad.WebsiteTests` runs the real merge and build scripts
through Node on test result files; those values are never published. Official
Three.js bytes and license remain pinned for the hero scene.
