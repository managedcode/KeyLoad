# SQL parser adoption proposal — KL-045 / KL-047

Current syntax authority: [ADR-130](../ADR/ADR-130-microsoft-scriptdom-tsql.md), [Microsoft ScriptDOM requirements](../Features/QueryExecution/ScriptDom.md) and [active T-SQL inventory](sql-client-conformance.json). PostgreSQL grammar/parser candidate selections and CALL/lexical implementation plans in this prior record are historical and inactive. Preserve exact original source/results and generic full-operation acceptance; they do not qualify T-SQL. Genuine PostgreSQL wire/Npgsql interoperability and benchmark workloads remain current independent contracts. No new parser, fallback or package adoption is authorized here.


Status: proposal N only. No dependency is selected or installed. Owner approval is required for every new library adoption; Microsoft libraries are preferred when they fit the required contract.

## Concrete comparison

| Requirement | Existing KeyLoad bounded parser | Microsoft ScriptDOM | Reviewed SqlParser-cs |
|---|---|---|---|
| Target | Executed, closed Q1/Q2 and canonical CALL profiles; full SQL remains open | SQL Server T-SQL syntax AST | Managed multi-dialect syntax AST including PostgreSQL; exact PG18 conformance unproven |
| Current LIMIT/OFFSET/FETCH | Existing canonical SqlWindowSyntax and real wholeflows | Current KeyLoad literal corpus must be evaluated; T-SQL window grammar is not a silent equivalent of Q1 | PostgreSQL dialect is a promising fit; exact current literals still need evaluation |
| CALL / model sources / MISSING | Existing canonical operation compiler and model/reference semantics | Requires explicit extension boundary or declared refusal; parsing EXEC is not equivalent to KeyLoad CALL | Native dialect extensibility may help; no current KeyLoad CALL/MISSING semantics demonstrated |
| Parameters | @named scalar parameters; native PG wire translates positional $n before canonical dispatch | T-SQL variable AST requires closed parameter lowering; native PG transport remains separate | Dialect-specific placeholders require exact scalar/slot lowering, not arbitrary variables |
| Locations | Current SqlToken retains Kind/Text/Quoted only; current safe errors have fixed messages | ParseError exposes offset/line/column and AST supports source structure | Reviewed tokenizer assigns token locations; exact exception/public mapping needs audit |
| Cancellation/resources | Original operation budget, byte/token/depth limits and periodic cancellation checks | Inspected blocking API lacks CancellationToken and first builds token collection; byte gate alone is insufficient | Reviewed wrapper has no CancellationToken; ParserOptions has recursion limit, tokenizer builds token list; end-to-end token/CPU/memory/cancel proof remains open |
| License/package/pin | Repository-owned EL2 distribution; no new package | MIT; actual adoption requires exact NuGet/package/source pin and .NET10 verification. Inspected main is mutable and not pinned | MIT; source e1929977a96290f937e9a2a67e3c823b7dc25cd2 reviewed; previous 0.6.5 package/source equivalence unverified |
| What it supplies | Existing bounded execution semantics with limited grammar | Broad mature T-SQL syntax/AST/diagnostic tooling | Broader managed syntax and custom dialect hooks; maintainers require subset verification |
| What it does not supply | Full SQL/client qualification | KeyLoad typed execution, authorization, RF3 or PostgreSQL grammar compatibility | KeyLoad semantics/authorization/RF3 or certified full dialect compatibility |

The Microsoft option deserves priority evaluation for a **declared T-SQL profile**. It is not yet a demonstrated replacement for ADR-065's PostgreSQL18 working target or the current Q1 dialect. PostgreSQL wire and SQL grammar are separate contracts; choosing T-SQL does not make the Npgsql transport invalid, but must be explicit to clients. There are no benchmark results favoring any option.

## Proposed adapter and approval boundary

Keep original connection admission, byte limits and original cancellation/deadline ownership. A selected, version-pinned parser produces syntax only. A closed one-statement visitor rejects unimplemented nodes, applies node/depth/parameter limits and lowers to existing canonical SelectQuery/AstQueryRequest/SqlOperationRequest. QueryValidation, fresh persisted binding, same-partition authority/read cuts, Orleans connection-owned CQRS and ZoneTree execution remain unchanged.

Malformed syntax maps to bounded Validation; valid unsupported families map to UnsupportedCapability. Expose numeric source location only through an explicitly frozen safe diagnostic contract, never raw SQL, credentials, user values or parser free-form messages. Multiple batches/GO, dynamic SQL and vendor DDL/DML are not admitted merely because a parser accepts them. Do not rewrite LIMIT into TOP or rerender normalized SQL and assume equivalent semantics.

Before production adoption: pin exact package/source/license; execute a fixed literal corpus covering current SELECT/Q2/CALL/model sources/MISSING/parameters/comments/Unicode/windows and malformed/deep/oversized inputs; prove original parse cancellation, disposal and resource bounds. A cancelled Task or postparse visitor does not prove parser work has stopped. Then implement the bounded lowering adapter and genuine SDK/MCP/native-driver RF3 fault→repair→healthy tests for each declared family. No parser alone completes full SQL.

The adoption decision remains pending exact package/version and corpus qualification. Any new library requires individual owner approval after that comparison is concrete. This proposal approves neither package and authorizes no dialect replacement.

## Primary evidence

- [Microsoft repository](https://github.com/microsoft/SqlScriptDOM), [MIT license](https://github.com/microsoft/SqlScriptDOM/blob/main/LICENSE), [blocking parser API](https://github.com/microsoft/SqlScriptDOM/blob/main/SqlScriptDom/Parser/TSql/TSqlParser.cs), [ParseError locations](https://github.com/microsoft/SqlScriptDOM/blob/main/SqlScriptDom/Parser/TSql/ParseError.cs).
- [Pinned SqlParser-cs README](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/README.md), [API](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/src/SqlParser/SqlQueryParser.cs), [options](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/src/SqlParser/ParserOptions.cs), [tokenizer](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/src/SqlParser/Tokenizer.cs), [MIT license](https://github.com/TylerBrinks/SqlParser-cs/blob/e1929977a96290f937e9a2a67e3c823b7dc25cd2/LICENSE).
- Current native KeyLoad SqlParser/SqlTokenizer/SqlToken/SqlSyntax/QueryValidation bodies retained in current-stage64-r37-kl045-scriptdom-scoped-assessment.original.json and the KL047 source review. Existing ADR-065 and sql-parser-candidate-review.md retain the original target and earlier research.

Source review only; no package installation, parser experiment, performance measurement, compiler or runtime qualification was executed.
