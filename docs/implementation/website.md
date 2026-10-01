# Public benchmark website

The static benchmark lab lives in `site/`. GitHub Pages serves it at `https://www.keyload.cloud/`. The apex `keyload.cloud` redirects to the canonical `www` address once GitHub Pages has accepted the DNS records and issued its certificate.

The domain uses GitHub's four apex A records (`185.199.108.153`, `.109.153`, `.110.153`, `.111.153`) and `www CNAME managedcode.github.io`. Existing mail, verification and Azure service records do not belong to this website. GitHub Pages uses Actions as its build source. The generated artifact includes `CNAME` with `www.keyload.cloud`; the repository Pages setting owns the custom domain for an Actions deployment. Follow the [GitHub custom-domain documentation](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site) for certificate and redirect behavior.

## Publication and evidence

`.github/workflows/pages.yml` runs after successful `KeyLoad CI` runs on `main`, or on manual dispatch. It checks out the measured source revision, downloads that CI run's `comparison-suite` artifact, validates complete schema-2 reports and publishes the static output. A failed/incomplete comparison or a source revision mismatch fails the website build. No synthetic measurements or local exploratory results enter this publication path. Manual dispatch selects the latest successful main push CI artifact.

Each site build includes three workload profiles: the correctness smoke, 1 KiB/eight-client/three-hop and 16 KiB/four-client/five-hop comparisons. The website offers six scenarios and eleven measures: useful throughput; p50/p95/p99 latency; failed attempts; enqueue/receive/ACK p99; generator CPU/attempt, allocations/attempt and observed RSS. Queue stage measures are available only for the queue scenario. Unsupported engine/scenario combinations retain null values.

The default aggregation is the median of per-repetition values with min/max whiskers. Latency aggregates are medians of run percentiles, not pooled percentiles. Users can inspect individual repetitions and download the original JSON, CSV and Markdown reports. Failure rates retain all selected attempts. Resource measures describe the load generator rather than database CPU or RAM. Development topology and qualification limits appear beside the charts and in every engine profile.

## Local verification

Use an isolated report directory containing only complete current schema-2 profiles:

```sh
node --test site/scripts/measurements.test.mjs
node site/scripts/build.mjs --reports=artifacts/site-reports --output=artifacts/site-preview
```

Serve `artifacts/site-preview` with a static HTTP server. Verify the first rendered desktop/mobile state, profile/scenario/measure/repetition controls, log scale, downloads and absence of console or network errors. Public delivery additionally requires a successful Pages deployment, valid HTTPS for both hostnames and an apex redirect to `https://www.keyload.cloud/`.
