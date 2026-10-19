# ScriptDOM GraphSearch capability profile

Canonical slice: `QueryExecution`. This focused correction extends [QueryExecution](../QueryExecution.md) and [ADR-130](../../ADR/ADR-130-microsoft-scriptdom-tsql.md).

## Contract and scope

The current ScriptDOM `GRAPH_SEARCH` table-valued function lowers to the same typed `GraphSearchRequest` as direct C# and official MCP callers. The capability manifest must report its current current read profile as `graph-search`. The read-profile identity is distinct from `SqlDialect`, which remains `TSQL.ScriptDOM180`, and from the existing AST and query-dialect version arrays. Adding this profile does not qualify full SQL or either client wire protocol.

## Requirements and acceptance

| Requirement | Measurable acceptance | Existing automated coverage |
|---|---|---|
| REQ-SDGC-001: advertise the implemented typed GraphSearch v1 read capability | AC-SDGC-001: `QueryEngine.Capabilities.ReadProfiles` contains `graph-search`; the catalog declares and uses this named constant exactly once | `SqlGraphSearchParityTests.CapabilityManifestAdvertisesSearchProfileWithoutChangingQ1Dialect`, with the dialect literal independently updated by the owning ScriptDOM consumer scope |
| REQ-SDGC-002: preserve the rest of the capability contract | AC-SDGC-002: `SqlDialect` remains `TSQL.ScriptDOM180`, `AstVersion` remains 1, both supported version arrays remain `[1]`, all previous read profiles, surfaces, predicates and injected database limits remain unchanged | The same genuine manifest test checks dialect, AST/version arrays and `Q2.InnerJoin`; complete before/after type review checks the unchanged catalog fields |
| REQ-SDGC-003: report actual operations without admitting obsolete syntax or hiding refusals | AC-SDGC-003: the canonical GraphSearch SQL operations retain direct-operation parity, multiple seeds, scope, retrieval, expansion, text/vector weights and empty selectors; wrong parameters, budgets, authority and cancellation retain their existing refusal and healthy-retry oracles | `SqlGraphSearchParityTests`, `SqlGraphSearchRejectionTests`, the existing cluster GraphSearch and three-way hybrid SDK/official MCP scenarios owned by the graph consumer scope |
| REQ-SDGC-004: qualify the integrated source rather than the proposal | AC-SDGC-004: exact-source compilation, TUnit unit/recovery/Aspire cluster and required Linux evidence are retained before claiming acceptance; a check-only result or profile entry alone does not pass these gates | Main integration owner retains original build/test/source identities and reports |

## Positive, negative and error flows

A current manifest includes the GraphSearch read profile while preserving the declared dialect and query versions. Valid SQL still reaches the separately authorized database operation and the existing bounded read-cut execution. Invalid function shapes, parameter kinds, unauthorized graph labels, excessive requests and cancellation continue through their owning strict diagnostics; the manifest change creates no reader, parser, role or fallback. Empty selector and zero-weight controls continue to use the same canonical graph path.

```mermaid
flowchart LR
    Manifest[Current query capability catalog] --> Profile[graph-search read profile]
    Execution[ScriptDOM GRAPH_SEARCH] --> Typed[Typed GraphSearchRequest]
    Direct[Direct C Sharp and official MCP] --> Typed
    Typed --> Authority[Persisted authorization and bounded execution]
    Authority --> Oracle[Existing complete operation parity oracles]
```

## Canonical slice map and boundaries

| Surface | Owned path or reason |
|---|---|
| Backend contract | `src/KeyLoad.Query/QueryExecution/Contracts/QueryCapabilityCatalog.cs` |
| parser | Existing `src/KeyLoad.Query/QueryExecution/Parsing/ScriptDomGraph*.cs`; unchanged by this correction |
| Unit tests | Existing `tests/KeyLoad.UnitTests/QueryExecution/Cases/SqlGraph/SqlGraphSearchParityTests.cs` and rejection/support roles; separate graph consumer owner |
| cluster tests | Existing QueryExecution GraphSearch and Search three-way hybrid scopes; separate graph consumer owner |
| SDK/MCP/CLI | N/A for new code: the existing query capability contract and graph operation transports are reused |
| Frontend/benchmark/infrastructure | N/A: no UI, measurement, orchestration or topology change |
| Documentation | `docs/Features/QueryExecution/ScriptDomGraphCapabilities.md` |

No persistence format, authorization, operation dispatch, resource limit, profile alias, parser compatibility branch or new dependency is introduced.
