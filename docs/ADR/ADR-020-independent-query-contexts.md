# ADR-020: Independent bounded query contexts

Status: Accepted; implementation and qualification pending.

## Context and decision

Each operation must have an independent query context and bounded work/deadline/cancellation state so one caller cannot block another. The optional client session facade in the product design may carry convenience state, but the connection grain (ADR-125) carries only bounded disposable session state and never becomes the source of query authority.

Use immutable request-scoped prepared state and one explicit execution budget. Any future SDK session facade remains client-side or delegates independent operations; it cannot freeze a shared server read cut across unrelated requests. Query context disposal releases iterators, views, and admission reservations on success, error, timeout, and cancellation.

```mermaid
sequenceDiagram
    participant Client
    participant Grain as Connection grain
    participant Query as Bounded query context
    participant Store
    Client->>Grain: Independent request and stable command identity
    Grain->>Query: Bind identity, budget, deadline, cancellation
    Query->>Store: One authorized read cut
    Store-->>Query: Bounded result
    Query-->>Grain: Result or explicit budget/cancel error
```

## Rationale, alternatives, and consequences

Unbounded shared contexts risk head-of-line blocking and stale authority. The connection grain keeps no shared read cut and no cached authority, which keeps cancellation and policy reauthorization simple. Separate operation contexts cost setup work but isolate budgets and preserve current authorization boundaries.

## Related requirements and implementation contract

Related: `REQ-ROUTE-001/AC-ROUTE-001`, `REQ-MP-002/AC-MP-002..006`, `REQ-SEARCH-004/AC-SEARCH-004`, and `REQ-QUERY-006/AC-QUERY-006`; ADR-036 is the mandatory Orleans foundation reference. Current source includes `src/KeyLoad.Query/QueryExecution/Queries/QueryEngine.cs`, `ReadExecutionBudget`, and per-operation routing contracts; target helpers stay in QueryExecution/Search feature slices.

1. Freeze context lifetime, shared-versus-operation budget rules, and cancellation propagation before changing APIs.
2. Test concurrent independent requests, cancellation at each stage, policy revocation between pages, and healthy subsequent operations using real storage and Kestrel/cluster.
3. Implement context-local immutable preparation and scoped resources; keep every operation's context independent inside the connection grain.
4. Keep query-context changes within the current persisted format; rollback removes only the optimization and retains request isolation.
5. Qualify real SDK and official MCP calls through GitHub TUnit/recovery/cluster suites; source-only build is not qualification.

Current files: `src/KeyLoad.Query/QueryExecution/Queries/QueryEngine.cs`, `src/KeyLoad.Core/ReadExecutionBudget.cs`, `src/KeyLoad.Orleans/CommandRouterGrain.cs`. Target paths are matching QueryExecution/Search feature folders. The router and integration join have a single owner; dependencies are ADR-036, Admission, Search and Authorization.
