# ADR-053: one light KeyLoad visual identity for the console and the site

Status: Accepted. Date: 2026-10-02. Owner: KeyLoad lead. Related: REQ-AD-008..010 / AC-AD-008..010 ([AdminDashboard](../Features/AdminDashboard.md)), REQ-BC-029 / AC-BC-029 ([BenchmarkComparisons](../Features/BenchmarkComparisons.md)), ADR-040 site design, ADR-051 admin console.

## Context

The owner rejected the visuals of both surfaces:
- The runtime console (`/admin`) used a dark mint theme, a text monogram and a single polyline chart.
- The public site used a warm-paper/forest editorial style with serif display type.

The two surfaces shared no logo, palette, typography or components, so KeyLoad read as two unrelated products. The owner asked for:
- one style across both surfaces, starting with a simple, clear, light and attractive console;
- clear charts;
- convenient menus and categories;
- error logs, data volume, performance and nodes.

## Decision

- **One brand source.** `src/KeyLoad.Server/Features/AdminDashboard/Assets/brand.css` holds the tokens and the shared primitives (`kl-brand`, `kl-btn`, `kl-card`, `kl-glass`, `kl-badge`, `kl-chip`, reduced motion). It is mirrored byte-for-byte to `site/Features/BenchmarkComparisons/brand.css`, and `logo.svg` to `site/favicon.svg`. The SiteTests drift test `AC_VI_001` fails on any difference.
- **Style direction (owner, 2026-10-02).** Earlier candidates were rejected: a light-blue look, pastel/rainbow glass and a plain grey version. The adopted style is serious Apple-style Liquid Glass in the sibling brands' colours:
  - Managed Code: warm off-white, black ink and system typography.
  - Prostir: graphite, with lime `#c6f24e` as the only brand accent.
  - Glass: translucent surfaces with specular top edges and large blur, capsule controls and large radii.
  - No Material Design and no decorative colour blobs.
  - The logo is a graphite squircle with white replica blocks and chevron, plus one lime replica.
  - Data colours come from the dataviz-validated categorical order (lime-ink, lilac, peach, sky, pink, amber, teal, red). Status colours are reserved.
- **Surfaces.**
  - The console is a light glass shell: transparent navigation with a lime selection indicator, an opaque glass top bar and a glass content layer.
  - The site has a dark graphite hero with a full-width Three.js scene, followed by light glass sections.
- **CSP and fonts.** System fonts only, no external fonts or CDNs. The console CSP is unchanged. Dynamic geometry uses CSSOM only.
- **Console information architecture.**
  - **Monitoring:**
    - Overview: KPI tiles with sparklines, a stacked request-activity chart, a storage donut, a node card, admission and recent errors.
    - Performance: throughput, latency and error-rate charts, lifetime totals and admission meters.
    - Errors & logs: the server failure log with status filters, plus session events.
  - **Data:** Catalog with kind chips, Collections, Queues and Blob storage.
  - **Infrastructure:** Nodes (RF3 voter figure and details) and Disk usage (category bar and largest files).
  - Every original `data-view` and pinned browser hook is preserved.
- **Real data only.** Charts plot at most 120 comparable session samples and reset on process change, failure or reconnect. The error log is a new bounded server observation: `AdminHttpSnapshot.RecentFailures`, at most 50 newest-first entries holding the route template, normalised method, status, aborted flag and duration. It never holds a raw path, query, payload or credential.
  - The voter figure uses configured membership (`AdminNodeSnapshot.LocalVoter`, `Voters`) and the reported leader. Peers are labelled as not observed from the executing node.
- **Site.** The landing is a new product page, not a restyle:
  - a floating glass capsule navigation;
  - a graphite hero with a lime headline accent and the Three.js RF3 scene (lime logical-partition gem, replication beams to three graphite hosts with lime/lilac partition tiles; about 22 draw calls and 290 triangles, no idle loop);
  - an architecture-facts strip;
  - a capability grid;
  - glass benchmark and methodology sections;
  - a graphite reproduce panel.

  Every tested hook, budget and lifecycle contract is unchanged.

```mermaid
flowchart LR
    Brand[brand.css and logo.svg canonical in console Assets] --> Console[admin console embedded whitelist]
    Brand -->|byte identical mirror| Site[site brand.css and favicon.svg]
    Drift[SiteTests AC_VI_001 parity] --> Site
    Drift --> Brand
    Server[AdminHttpMetrics ring of 50 failures] --> Snapshot[AdminNodeSnapshot]
    Config[Replica voter membership] --> Snapshot
    Snapshot --> Console
```

## Alternatives

- Restyle each surface separately. Rejected because drift returns.
- A shared npm or MSBuild design package. Rejected: it adds tooling and dependencies, the Docker context holds only `src/`, and the site build is a separate pipeline.
- Inline the logo into each page. Rejected because the favicon and logo bytes must stay one fact.

## Implementation contract

1. **VI-L (lead):** brand tokens and logo, mirrored to the site. Done before the UI packets.
2. **VI-B (worker, sonnet):** ring buffer and middleware in `src/KeyLoad.Server/Features/AdminDashboard/AdminHttp*.cs`, with unit tests `AdminHttpMetricsTests` and `AdminHttpMetricsMiddlewareTests` written first. The DTO is frozen by the lead in Abstractions.
3. **VI-D (lead):** console `Assets/*` and the `AdminStaticAssets` whitelist. The lead also adds the voter membership fields and observer, and owns the `AdminDashboardRf3Tests` assertions.
4. **VI-S (lead):** site HTML/CSS/poster, colour constants in `measurements.mjs` and `scene-geometry.mjs`, `BUILD.assets` and SiteTests parity.
5. **VI-J (lead):** feature docs, this ADR, the Architecture map and exact-SHA CI evidence.

Dependencies:
- Contract additions are additive init properties with defaults. The SDK and the official MCP output pick them up from the DTO type.
- No schema, storage, routing or package change.

Migration, rollback and verification:
- **Rollout:** a single release. The console and the site deploy independently.
- **Rollback:** revert the assets and remove the three init properties. Stored data is untouched.
- **Verification:**
  - ci.yml unit tests: metrics ring and middleware.
  - RF3: failure-log route template and voter membership through the real SDK and official MCP.
  - Real Chrome console flow: existing hooks plus new views.
  - pages.yml SiteTests: unchanged hooks, budgets and the parity test.
  - Subjective beauty is judged from desktop/mobile screenshots of a real RF3 cluster (manual exception). It never replaces a functional gate.

Join points: shared enum/DTO edits are lead-only, and backend and UI write scopes are disjoint. This ADR stays Accepted until exact-SHA unit, RF3 browser and site qualification pass.
