# Benchmark collection and naming repair

Goal: finish the diagnosed public harness contract prerequisites without changing
the deterministic workload or published schema3 JSON. This is part of the already
authorized strict-analysis and memory/performance repair, not a database API change.

Current evidence: the ordinary dependency-enabled ComparisonHost Release build in
`/private/tmp/keyload-performance-baseline/strict-comparison-adapter-join.log` fails
with 16 errors, zero warnings. Nine public array properties expose mutable corpus,
oracle or report storage; the enum and exception names fail enabled SDK rules.
Three PostgreSQL CA2100 findings need their own SQL implementation contract.

Options: suppress rules or wrap arrays with mutable interfaces (reject: hides the
defect); clone every access/report (reject: extra allocations and mutable truth);
use the already established ImmutableArray contract with exclusively owned buffers
and preserve serialized arrays (chosen). Rename CLR Single to Standalone while
retaining the existing configuration/JSON Single spelling; replace ComparisonFailure
with a standard exception type, preserving coded messages and safe formatting.

Parallel scopes: first-authored real contract fixtures; one owner for shared harness
contracts/corpus; another owner for engine result/vector consumers; lead for report,
runner, lifecycle tests, shared callers and documentation. Contract writes wait for
the regression packet. Caller writes wait for stable contract signatures. SQL and
numeric quality gates stay separate. Same-file writes are serialized.

Risks: default versus empty ImmutableArray; absent optional arrays; JSON converter
policies; configuration binder uses TypeDescriptor, not JSON naming metadata;
caller-owned buffers; benchmark oracles must keep scalar accumulation and ordering;
report samples remain mutable until post-measurement validation finishes. No CI
runtime proof exists for these new sources yet.

Recommendation: explicit accepted contract and test matrix, real serializer/config
and corpus fixtures before source migration; freeze exactly once, reuse the public
strict converter through its registered converter without importing all product
serializer settings. Preserve report bytes and all engine/wire measurement values.
