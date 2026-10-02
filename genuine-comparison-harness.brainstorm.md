# Genuine comparison harness and Neo4j cleanup

Current source: ComparisonHarnessTests has three useful pure oracle methods and
three prohibited fake target/HTTP cases. The real ComparisonTests already starts
the pinned Aspire Neo4j container and real RF3 database, so a genuine replacement
can reuse that lifecycle after the measured report, without a second cluster.
Current full solution qualification remains blocked by source/compiler/native
integration debt; the historical successful main run does not qualify new code.

The owning Neo4jTarget sets cleanup authority before its CREATE CONSTRAINT has
succeeded. A duplicate native constraint-name error can therefore lead DisposeAsync
to drop a constraint that the failed target never acquired. This is KeyLoad-owned
code, so repair it here with a genuine server regression before the source fix.

Options: retaining or renaming doubles violates mandatory policy. Adding a product
failure injector, session wrapper or second Aspire fixture introduces unnecessary
boundaries. Use independent real query clients and existing runner progress observer
to prepare controlled, private data between setup and timing. Keep fake reports out
of benchmark evidence and retain every original oracle obligation explicitly.

Recommended scope: migrate all three pure corpus/statistics/graph methods once to
the canonical BenchmarkComparisons unit slice; replace the fake Neo4j error,
correct/mismatched/setup-failure and warmup/report cases with genuine existing-
container checks. Prove native HTTP202 error and code-only redaction by creating
an exact duplicate constraint name on a different private marker label; preserve
that constraint and marker after failed target disposal. Cleanup authority starts
only after acknowledged successful target CREATE. Lost acknowledgements remain
fail-closed and may leave an unowned artifact; no new idempotence, general namespace
collision, fault-recovery or production-durability guarantee is claimed.

Use existing finite synchronous HttpClient.Send only inside the already synchronous
progress observer to change the owned label's document JSON before measured point
reads, then restore it before writes. No blocking async bridge or new product hook.
Good runs retain two repetitions, twelve operations and two warmups. Actual unique
DocumentWrite IDs and exact JSON prove all twenty-eight warmup/measured writes
persist, while only twenty-four write attempts enter samples; CSV retains every
supported scenario attempt. Unsupported and setup-failed cases retain null
measurements and empty samples, with independent explicit support counts.

Risks/open decisions for read-only discovery: genuine Neo4j native code/status,
credential retrieval via actual Aspire13.6 ParameterResource.GetValueAsync,
client/response/stream ownership and callback failure cleanup, exact write-ID
formula, safe secret assertions, source numeric limits and replacement mapping.
Root alone owns docs/ADR/contracts and the minimal existing ComparisonTests hook;
independent pure migration, real helper and Neo4j source repair need explicit
disjoint accepted contracts before writes. Existing schema3/nine-engine/35+31
Docker topology and current broad stale integration assertions remain mandatory
pending work; this stage cannot silently reduce or infer their counts.

No local tests, benchmarks, Docker startup, extra packages, suppressed findings,
public API/wire/corpus/measurement changes or publishable resource improvement.
Create detailed acceptance then ordered plan and lifecycle ADR before implementation.

## Query API acknowledgement correction and bounded discovery

Official Query API success examples omit the errors property and include a data
object with fields/values arrays. Requiring an explicit empty errors array would
reject valid real Neo4j responses. Any later acknowledgement validation must accept
the documented omission, reject native nonempty errors, and fail closed on malformed
claimed success. Missing/lost acknowledgements must not grant cleanup authority.
The parser and test boundary need an exact accepted contract before source edits.

| Task | Owner / permission / scope | Start, result and join |
|---|---|---|
| TASK-GH-ACK-R | Existing economical capable CLI worker; read-only primary Neo4j2026.09 Query API success/error writer and current Neo4jTarget lifetime | Resolve actual emitted schema-write response shape (data.fields/values, omitted errors, bookmarks), robust ownership acknowledgement and JsonDocument/stream failure ownership. No edits/build/tests/runtime/container/proxy/fakes. Propose narrow source/parser test ownership with real response or pure protocol validation, preserving public adapter ABI/wire/measurement; lead accepts AC/ADR/graph before any implementation. Stop on unresolved pinned semantics. |
