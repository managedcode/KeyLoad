# Source field lineage and independent grants

KL-048 / QueryExecution. This bounded acceptance stage covers single-document source paths, quoted dotted keys and numeric array extraction. It does not duplicate KL-097 composed model projections or change KL-047 parsing.

REQ-KL048-FIELD-001: Project protected source fields only after persisted RawRead authorization; output aliases never change source authority. A public source renamed to a protected-looking output remains its public value. Wildcard array field policy applies to a numeric array component.
REQ-KL048-FIELD-002: RawUse grants authorize filter and sort access independently of RawRead. Read-only may project the secret but cannot filter or sort by it; use-only may filter and sort while selected secrets stay null and retain original redaction paths.
REQ-KL048-FIELD-003: A live QueryEngine must observe every advanced principal epoch, whole-principal revocation and repair without rebuilding the engine. Static none/read/use/both principals retain the same truth table after restart.

AC-KL048-FIELD-001: Actual T-SQL, direct AST, JSON AST and generated native AST return the same literal complete QueryPage: entity/revision, all projected values, original classification paths, read cut, null cursor and bounded-full-scan access path. Complete original ZoneTree records and position remain unchanged around successful and refused reads.
AC-KL048-FIELD-002: LOWER, CAST and COALESCE projection attempts fail with exact native UnsupportedCapability diagnostics and no returned page. These are intentionally unsupported expressions, not implemented function whitelist/type inference or proof of zero metadata I/O.
AC-KL048-FIELD-003: Exact original mutation/receipt fields and same-ID replay survive two genuine existing-only opens of the same root, including full image, position and identity equality. All owned resources join and independent failures remain retained.

This is newly authored Unit coverage over real native components, not runtime evidence. Semantic compilation, full original runtime execution/deadline fit, Linux qualification, SDK/official MCP RF3, complete binder type inference and SQL conformance remain OPEN. No product defect or performance improvement is claimed from this source packet.
