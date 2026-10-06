# Public benchmark website

The static benchmark lab lives in `site/`. GitHub Pages serves it at `https://www.keyload.cloud/`. The apex `keyload.cloud` redirects to the canonical `www` address once GitHub Pages has accepted the DNS records and issued its certificate.

The domain uses GitHub's four apex A records (`185.199.108.153`, `.109.153`, `.110.153`, `.111.153`) and `www CNAME managedcode.github.io`. Existing mail, verification and Azure service records do not belong to this website. GitHub Pages uses Actions as its build source. The generated artifact includes `CNAME` with `www.keyload.cloud`; the repository Pages setting owns the custom domain for an Actions deployment. Follow the [GitHub custom-domain documentation](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site) for certificate and redirect behavior.

## Publication and evidence

The separate [Website workflow](../../.github/workflows/website.yml) owns static
site qualification, generation and Pages deployment. It runs on trusted main
pushes, manual dispatch and completed own-main Benchmarks events. Build and Tests
(`build-and-tests.yml`) builds the complete solution and runs ordinary project tests; its
result is not a dependency of Website. Benchmarks produces authenticated JSON.
Release remains the prepared manual product-release workflow.

Website selects the latest ready authenticated Benchmarks aggregate. With data,
it preserves original measured/control/website revisions, immutable archives,
failed/null cells and the complete measured site/browser/coverage gates. Without
ready data it qualifies the content-only product site and emits no metric catalog,
figures or synthetic measurements. New ready data triggers a fresh website run.
Source freshness and the selected ready tuple or absence are checked before the
needs-gated, least-privilege Pages deployment. Invalid selected evidence and
provider/authentication errors remain failures; absent data is not a failure.

The canonical HTML/modules/styles live in `site/Features/BenchmarkComparisons/`;
`site/scripts/build.mjs` is the thin build entry. Actual TUnit/MTP tests run through
the Aspire-owned `site` entry and exercise real Node/Chrome operations. Official
Three.js bytes and license remain pinned and verified. Original measurements
retain their own workload/topology/resource contracts; no historical or local
result qualifies current database behavior.

[BenchmarkComparisons requirements](../Features/BenchmarkComparisons.md) and
[ADR-112](../ADR/ADR-112-independent-website-publication.md) own the publication
contract. [status.json](status.json) records actual implementation and qualification;
workflow source and local checks alone do not establish successful Pages delivery.
