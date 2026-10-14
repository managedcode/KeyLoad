# RowVisibilityCache responsibilities

Canonical slice: CodeQuality. Product owner: Core/Features/Authorization. Applicable contracts: [CodeQuality](../CodeQuality.md) REQ-CQ-006 / AC-CQ-008 and REQ-CQ-MULTILINE-BLOCKS-001 / AC001..003, [Authorization](../Authorization.md) REQ/AC-AUTH-005/007, [ADR-033](../../ADR/ADR-033-code-quality.md) and [ADR-014](../../ADR/ADR-014-principals-rbac-row-policy.md).

REQ-CQ-ROW-CACHE-001 / AC-CQ-ROW-CACHE-001: cache and retirement types remain within200 code lines, methods64, files400, nesting3 with multiline executable braces. Actual canonical formatter/verify and analyzer-enabled Release compilation must accept this responsibility extraction without suppressions or changed thresholds.

REQ-CQ-ROW-CACHE-002 / AC-CQ-ROW-CACHE-002: RowVisibilityCache retains its SAME native Lock, budget, owner reservation and state. Its existing Sweep callsites delegate to feature-local RowVisibilityRetirement while still holding the SAME original lock. The helper receives the existing head and release state by reference; it creates no retained collaborator, charge, lock or unbounded cache. Entry disposal is attempted only when retired and unpinned; unlink only after successful disposal; pinned or uncertain ownership remains charged. Closing admission and owner-release failure latching retain exact original ordering and failure ledger.

REQ-CQ-ROW-CACHE-003 / AC-CQ-ROW-CACHE-003: retain exact native scoped read identity, complete-set publication, current positive-row authorization, admission-before-allocation, pressure retirement and final lease disposal. Original GenerationVisibilityTests whole operations (pressure, cancellation, persisted revoke/repair, full literal SQL/graph rows, receipt replay and same-root cold) remain the behavior mapping. No new test, public/internal caller signature, role, read cut, storage format, operational bounds, timeout or lifecycle contract.

```mermaid
flowchart LR
    Request[Original scoped cache operation] --> Gate[Same cache Lock]
    Gate --> Cache[Original identity admission and pins]
    Cache --> Retirement[Feature-local retirement under same Lock]
    Retirement --> Ledger[Original disposal and owner failure ledger]
    Ledger --> Checks[Numeric multiline and original operation gates]
```

UI, API/wire, persistence, dependency adoption, topology and performance claims: N/A, no changes. Authored Source-N code is reviewable; current native checkOnly, new type/path absence, compilation, original operation runtime and Linux qualification remain separate pending gates. Source review does not prove runtime or acceleration. No source-text assertions substitute for original operations.
