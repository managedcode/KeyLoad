# Public benchmark website

The static benchmark lab lives in `site/`. GitHub Pages serves it at `https://www.keyload.cloud/`. The apex `keyload.cloud` redirects to the canonical `www` address once GitHub Pages has accepted the DNS records and issued its certificate.

The domain uses GitHub's four apex A records (`185.199.108.153`, `.109.153`, `.110.153`, `.111.153`) and `www CNAME managedcode.github.io`. Existing mail, verification and Azure service records do not belong to this website. GitHub Pages uses Actions as its build source. The generated artifact includes `CNAME` with `www.keyload.cloud`; the repository Pages setting owns the custom domain for an Actions deployment. Follow the [GitHub custom-domain documentation](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site) for certificate and redirect behavior.

## Publication and evidence

`.github/workflows/pages.yml` has separate candidate validation and publication modes. Manual dispatch defaults to `validate`: it authenticates the selected successful main-push `KeyLoad CI` run, downloads its `comparison-suite`, builds/runs the full independent TUnit site suite and retains a preview artifact without deployment. The catalog distinguishes the candidate website SHA from the historical measured SHA and records raw report hashes. Merely supplying an evidence URL to the builder does not authenticate the run.

Publication still checks out the successfully measured source revision and requires its complete site suite and schema-2 reports. A failed/incomplete comparison or a source revision mismatch fails the website build. Historical source predating the site migration cannot satisfy this new gate; the redesign task does not deploy or change DNS. Publishing new website source against older measurements needs the separate two-revision publication contract in ADR-040. No synthetic measurements or local exploratory results enter either path.

Each site build includes three workload profiles: the correctness smoke, 1 KiB/eight-client/three-hop and 16 KiB/four-client/five-hop comparisons. The website offers six scenarios and eleven measures: useful throughput; p50/p95/p99 latency; failed attempts; enqueue/receive/ACK p99; generator CPU/attempt, allocations/attempt and observed RSS. Queue stage measures are available only for the queue scenario. Unsupported engine/scenario combinations retain null values.

The default aggregation is the median of per-repetition values with min/max whiskers. Latency aggregates are medians of run percentiles, not pooled percentiles. Users can inspect individual repetitions and download the original JSON, CSV and Markdown reports. Failure rates retain all selected attempts. Resource measures describe the load generator rather than database CPU or RAM. Development topology and qualification limits appear beside the charts and in every engine profile.

## Design and implementation boundaries

The canonical HTML/modules/styles live in `site/Features/BenchmarkComparisons/`; `site/scripts/build.mjs` is the thin build entry. The product introduction precedes the benchmark workspace. Three.js 0.186.1 and its MIT license are pinned, integrity checked and served from the same origin. The lazy RF3 illustration is conceptual architecture, with physical hosts and logical partitions distinguished; it is never live telemetry or measured performance. A static illustration and readable content remain when graphics or JavaScript is unavailable. See [ADR-040](../ADR/ADR-040-static-site-threejs-evidence.md), [acceptance](../ADR/ADR-040-static-site-threejs-evidence.md) and [the frozen protocol](../../site/Features/BenchmarkComparisons/protocol.md).

## Development preview and GitHub qualification

Only development static builds and the explicit manual browser/design review are permitted locally. Use authentic downloaded successful-CI schema-2 profiles and a nonexistent isolated output:

```sh
node site/scripts/build.mjs --reports=artifacts/github-results-9c570f8 --output=artifacts/site-preview-new --revision=9c570f8c33a7a9667507a8e1c0ca68860de3be45 --evidence-url=https://github.com/managedcode/KeyLoad/actions/runs/36926803549
```

The preview explicitly marks website source as an uncommitted working tree. Serve the generated directory with an isolated static HTTP server. Retain first-render desktop/mobile screenshots, keyboard/controls/download observations and real graphics backend/lifecycle evidence tied to source hashes. This is manual design evidence, not test or database qualification. No local Node test runner, TUnit execution, recovery/load qualification is allowed.

The qualification project is `tests/KeyLoad.SiteTests`, TUnit/MTP/.NET10, with central analyzers and no Core/AppHost dependency. It invokes actual production Node modules and independently computes C# numeric expectations over authentic reports. Controlled corrupt copies exercise validation without becoming published measurements. Run the full suite only in the workflow; retain its candidate SHA, measured SHA, run/job links and artifacts. Coverage/complexity collectors remain separate pending gates. Public delivery additionally requires a successful Pages deployment, valid HTTPS for both hostnames and an apex redirect to `https://www.keyload.cloud/`.
