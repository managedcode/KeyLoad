# SQL parser candidate source review

TASK-SQLC-R12 / REQ-SQLC-001/006/009 under Accepted ADR-065.
Read-only primary-source research, 2026-10-03. Parser selection, package
installation, syntax execution and native client qualification remain pending.
This review complements the183-command inventory; it does not prove it passes.

|Candidate / inspected source|Finding and remaining evidence|
|---|---|
|[SqlParserCS](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/README.md), source `e1929977a96290f937e9a2a67e3c823b7dc25cd2`|Managed C# syntax AST, multiple dialects; maintainers explicitly describe incomplete dialect support and no comprehensive conformance assessment. Source targets net10, but stable package0.6.5 equivalence to this source is unverified. No typed execution or demonstrated bounded PG18 conformance.|
|[libpg_query18.0.0](https://github.com/pganalyze/libpg_query/releases/tag/18.0.0), source `204fbdbd3ed5f8691ab358e49f1fc5397b4679e2`|Tagged PostgreSQL18-derived native C parser. Strongest inspected version-specific syntax reference, an integration-owner inference from primary source. Parse structures alone do not bind KeyLoad schemas/types, authorize plans or execute operations. Native packaging/lifetimes/resource limits require a separate accepted boundary contract.|
|[pgsqlparser](https://github.com/mysticmind/pgsqlparser-dotnet/blob/cf04a7e14fc0adf51e6ac2d834a91d72ecb1d14a/README.md), source `cf04a7e14fc0adf51e6ac2d834a91d72ecb1d14a`|.NET wrapper with bundled native libraries and parse-tree API. Inspected README example reports parser170005; exact published package/native-source PG18 provenance is unverified. Async APIs alone do not prove interruptible native parsing.|
|[GrepSQL](https://github.com/jonatas/grepsql/blob/db641f1f017f33b6815038b094e9e779d53d4b15/README.md), source `db641f1f017f33b6815038b094e9e779d53d4b15`|Native wrapper/AST tools. Inspected README example reports170004; RID declarations are not executed deployment evidence. No verified PG18 package binding.|
|[PostgresQuery0.1.4](https://www.nuget.org/packages/PostgresQuery/0.1.4)|Syntax wrapper; exact source/native parser revision is unverified. Package documentation explicitly leaves Windows/macOS native binaries untested. No KeyLoad interoperability proof.|

Root retains the PostgreSQL18 working target without selecting a parser.
Investigate the tagged upstream parser and exact wrapper/native provenance first;
evaluate a managed alternative only against a maintained differential corpus.
Neither a broad dialect list nor accepting a command completes full SQL support.
No exact missing-construct matrix or comparable measurements were established.

Before implementation freeze SQL byte/token/depth/AST/node limits, allocation and
CPU budgets, thread safety, ABI/RIDs, allocator/free ownership, native failure
containment and cancellation/drain semantics. Cancelling a Task does not establish
that native work stopped. Parsing cannot bypass end-to-end admission or error
contracts. KeyLoad still owns typed binding, null/type/coercion semantics,
persisted authority, read cuts, bounded plans and canonical RF3 mutations.

```mermaid
flowchart LR
    Input[Bounded SQL input] --> Parser[Version pinned syntax parser]
    Parser --> AST[Bounded syntax structure]
    AST --> Binder[KeyLoad typed schema and authority binding]
    Binder --> Plan[Bounded effect aware plan]
    Plan --> Execute[Canonical request and RF3 execution]
```

Qualification requires real PG18 differential syntax/execution cases plus negative,
malformed, nested, oversized, cancellation and resource fixtures. Public RF3 SQL
and native driver flows must independently qualify every advertised family.
Parser performance needs actual comparable GitHub measurements; no tests,
runtime/native calls, packages or dependency changes were performed by this review.

Research packet SHA256 `6128727b4fd8eda9bbb964fe94b5b79d8b6042205a24ac0e4af08f2f0284737f`;
machine-readable source inventory SHA256
`0622f3a6469da40262ffc9009847f3055aa2fd06cdf6246999ec87206e6a7b9e`.
Pinned upstream links remain the durable source references; private research is
manual architecture evidence, not product qualification or uploaded CI artifacts.
