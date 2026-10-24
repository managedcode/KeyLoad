# ADR-051: same-origin read-only administration console

Status: Accepted. Date: 2026-10-02. Related: REQ-AD-001..007 / AC-AD-001..007; ADR-036 Orleans routing, ADR-035 resource bounds, ADR-038 blobs and ADR-039 official MCP.

## Decision and rationale

Host the credential-free console shell and finite same-origin asset whitelist in the Server `AdminDashboard` slice, using Blazor Server with Microsoft Fluent UI Blazor components. Static styles, branding and the visibility bridge use an exact embedded whitelist; framework and Fluent assets use the SDK static-asset manifest. Protect all data reads with existing persisted identity and current administrator authority through the connection grain. The public Pages site retains its benchmark-evidence boundary. The approved UI dependencies are Microsoft.FluentUI.AspNetCore.Components 5.0.1 and test-only Microsoft.Playwright 1.63.0, both MIT. They do not introduce a schema or storage owner.

Catalog and queue metadata are gated bounded read pages. Physical file observations and process HTTP counters are borrowed from the executing node; report that node explicitly, incomplete scans and process resets. The server circuit polls sequentially and retains bounded measured history; it does not claim cluster totals or persistent metric history. Existing authorized query AST and blob-list operations remain their current owners.

```mermaid
classDiagram
    class AdminDashboardApi
    class AdminNodeSnapshot
    class AdminNodeObserver
    class AdminHttpMetrics
    class ConnectionGrain
    class ZoneTreeOwner
    class AdminCatalogReader
    class AdminQueueReader
    AdminDashboardApi --> ConnectionGrain : signed connection call
    ConnectionGrain --> AdminCatalogReader : current admin and read cut
    ConnectionGrain --> AdminQueueReader : metadata only
    ConnectionGrain --> AdminNodeObserver : borrowed admin capability
    AdminNodeObserver --> ZoneTreeOwner : node local observation
    AdminNodeObserver --> AdminHttpMetrics : process counters
    AdminNodeObserver --> AdminNodeSnapshot : bounded honest result
```

## Implementation contract

1. The frozen DTO/protocol, acceptance, feature, shared enum/routing/composition, SDK/MCP and architecture documents are one contract. Existing enum numeric values remain intact by appending three read kinds.
2. New Core/AdminDashboard readers, new Server/AdminDashboard C# observers/metrics/API, plus the INodeAdministration and NodeAdministration bridge. Each request establishes a fresh applied barrier, reloads authority and uses bounded gated reads; filesystem observation retains node ownership with one concurrent scan, 2,048-entry/250 ms/200-output bounds and no symlink following.
3. Colocated Server/AdminDashboard/Components implement the accessible desktop/mobile console and memory-only session against the frozen DTOs, existing query/blob operations, pagination, bounded real charts and disconnect/stale protections.
4. New matching UnitTests and IntegrationTests slices derive failing regressions from AC before production joins and exercise actual file stores, the replicated cluster, .NET/official MCP clients and the browser. Browser cases use native Playwright locators and real server operations; no hand-written CDP transport remains.
5. Scoped relevant source is delivered through normal Git. Qualification occurs only through exact-SHA GitHub Actions with real artifacts; baseline failures and absent coverage remain explicit. The ADR stays Accepted until every required AC, documentation and qualification gate passes.

## API and rollout

Add GET `/admin/dashboard`, POST `/admin/dashboard/resources` and POST `/admin/dashboard/queue`, their typed SDK methods and explicit read-only official MCP tools. The `/admin` redirect, `/admin/` component page and named assets are credential-free; the framework maps its circuit transport and finite manifest assets; data paths receive no auth exemption. HTTP telemetry counts completed `/v1` and `/mcp` requests excluding dashboard polling, status observations, static, health and internal routes; metrics contain only global process counters and elapsed duration, no sensitive labels.

Rollout needs coordinated server/SDK catalog support and leaves stored data unchanged. Rollback removes the dashboard slice and composition in one release, never renumbers existing reads or changes stored data. Public operation contracts, Orleans placement, distributed directory and activation repartitioning remain as configured. Physical observations are time-bounded filesystem metadata, not a durability proof. Manual visual rendering closes only subjective design; production/endurance/power-loss/coverage gates remain open until actual proof.
