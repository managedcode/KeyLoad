# KeyLoad website design

Date: 2026-10-02. Owner request: redesign the website to suit the product, add Three.js, think through the design and use installed Claude CLI with Opus 5.5 if available. Scope belongs to `BenchmarkComparisons`; the existing product specification, evidence and authorization boundaries remain mandatory.

## Problem and outcome

The current page begins with a six-engine benchmark dashboard. It does not give KeyLoad a clear product identity, explain its data model or show the RF3 ownership concept. Typography, spacing and navigation need a deliberate desktop/mobile composition. The benchmark controls, raw JSON provenance and honest unsupported measurements must stay central and functional.

Readers should first understand KeyLoad, then inspect measured evidence and the exact conditions. A Three.js architecture illustration can make the relationship between logical atomic partitions and three physical hosts easier to see. It must be labeled conceptual, never live topology or benchmark telemetry.

## Options and intended direction

- Retheme the existing table only: low migration cost, but insufficient product story and no architectural visual.
- Replace with a framework marketing template: unnecessary runtime/build migration and risk to evidence parsing; reject.
- Product-first static site with a distinctive visual identity, an RF3 Three.js scene and the existing evidence explorer: recommended. Use high-contrast ink, warm paper and a restrained signal accent; precise typography, fewer generic cards, clear status and source links.

The initial design direction is an editorial technical product page: generous hero, architectural scene, a concise feature narrative, dense readable evidence explorer, visible conditions, methodology, engine guarantees and documentation/source entry points. Claude Opus 5.5 contributes a bounded design critique; strongest native planning and final review own integration. No new model/tool/skill installation is authorized.

## Independent work and boundaries

Strongest planning/review: read-only source, policy and primary dependency research. After frozen REQ/AC/ADR/plan, disjoint workers can own the HTML/CSS composition and Three.js scene. Root owns existing dashboard migration, builder, shared contracts, docs, workflow and combined evidence. All new feature-owned assets use `site/Features/BenchmarkComparisons/`; root `index.html` remains the composition entry.

Data stays exclusively successful GitHub JSON. No hand-written measurement, fabricated winner, new benchmark, backend, credential, license feature, DNS change or database runtime change is in scope. Schema2 is historical and schema3 is pending unless its complete publisher contract is integrated and qualified; redesign must not invent evidence to fill future engines.

## Risks and open decisions

- Confirm exact Three.js release, official ESM artifacts, MIT license, integrity and same-origin hosting. Native renderer/backend support and teardown need a documented contract.
- Prevent graphics from blocking reading, report loading or interaction. Respect reduced motion, hidden tabs, offscreen state, small screens and resource bounds. The static content remains the actual product UI.
- Preserve keyboard tabs, focus, contrast, responsive table scrolling and every existing supported metric/repetition/profile.
- Current Pages uses Node tests despite mandatory root TUnit. Resolve or explicitly retain the unqualified migration debt; do not add another test framework or run local product tests.
- Other owners are changing source/docs in this dirty checkout. Keep disjoint ownership and inspect every diff without replacing their changes. No worktree.
- Exact-source CI and publication depend on a reviewable scoped delivery; current unqualified database changes must not be swept into website delivery.

Next: join TASK-SITE-PLAN-001, record stable acceptance IDs and an ADR implementation contract before any write-capable worker starts. Manual rendered browser proof is a design-review artifact; it does not replace GitHub test qualification.

The candidate also requires its centrally attached analyzer dependency to be qualified. Strongest TASK010 reviewed native MTP collector18.11.2 and a fail-closed PowerShell/.NET XML count gate. Reuse the runner and existing collector, freeze all analyzer sources, require every executable file and each diagnostic pipeline, and retain original XML. This bounded dependency substage does not attempt unfinished product/RF3 coverage or invent a historical baseline.

## Observed builder failure and fresh-evidence publication extension

The actual first SiteTests run36994330874 rejects unchanged official vendor bytes
because the builder compares a historical Node26 gzip length with Node22 output.
Keep immutable raw identity checks; validate recorded compression as historical
metadata and measure current gzip separately with Node/zlib provenance. Changing
vendor bytes, its manifest, asset budgets or coverage thresholds would conceal
the defect. Existing genuine positive build tests are the failing regression
baseline. Retain bounded child-process diagnostics and require negative tests to
identify their intended rejection, rather than accept an unrelated exit1.

The owner's subsequent instruction adds a separate site deployment workflow
that gathers fresh JSON from the actual GitHub comparison job after tests. Keep
the CI producer separate from Pages. A successful CI completion wakes Pages;
authenticated run/job/artifact selection, complete site qualification and a clean
build precede deployment. All numeric UI/chart values still come from report
bytes. Separate site-source and measured-source revisions; never treat a caller's
run URL or the legacy publisher's CLI flags as authentication. DNS remains out of
scope. Strongest publication planning will freeze the freshness/ordering contract
before its implementation; no obsolete publish path is acceptable as proof.
