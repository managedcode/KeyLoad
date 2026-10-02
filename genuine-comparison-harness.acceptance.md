# Genuine comparison harness acceptance

Goal: the comparison harness is qualified through its real public runner and
actual pinned Neo4j, with cleanup authority limited to resources it acquired.
Chosen direction: [brainstorm](genuine-comparison-harness.brainstorm.md).
Owner: BenchmarkComparisons / REQ-BC-023 and ADR-049. This is part of the authorized
strict-quality and resource/lifetime repair, not a new benchmark evidence profile.

## Scope and boundaries

In scope: migrate the three pure corpus/statistics/graph tests exactly once;
replace three prohibited fake target/HTTP tests with genuine existing-container
regressions; harden Neo4j response acknowledgement and cleanup ownership; preserve
the report, workload, library ABI and measurement contracts. Root alone integrates
the existing ComparisonTests entry, metadata/test visibility, docs and gates.

Out of scope: new packages, containers, fault proxies/injectors, service doubles,
paid features, product storage/Orleans/API/authorization changes, report schema or
sampling arithmetic changes. Schema3/nine-engine/35 Single plus31 Replicated
support obligations remain under ADR034/044. The old schema2/six-target broad test
is migration debt and must be coherently joined before qualification, not weakened
or used to derive smaller expected counts. Native/blob work in another chat is
protected. Tests/load/process/container execution is GitHub-only.

Actors: CI TUnit runner, real Neo4j administrator client, benchmark target/session
and report reader. Entry: existing ComparisonTests invokes one internal
Neo4jHarnessRegression.VerifyAsync endpoint/password/image/token helper after the
main measurement process exits, while its actual Aspire container remains alive.
The helper owns independent real inspection clients; Neo4jTarget owns its client.
Retrieve the actual secret with Aspire ParameterResource.GetValueAsync(token),
keep it in memory, and use the actual allocated endpoint and digest-pinned image.
No credentials/native message/private label may enter logs or assertion output.

## Criteria

- AC-GH-001 / REQ-BC-023: all three original pure method names and every original
  input/assertion/argument occur once under UnitTests/Features/BenchmarkComparisons.
  Small options retain16 documents,12 operations,2 warmups,2 repetitions,
  concurrency2,dimensions8,topK3,payload128. Preserve seed/hash difference, exact
  payload bytes, nearest-self, JSON equivalence/mismatch, invalid topK17, three
  attempted samples with timeout/p99/useful throughput/message/enqueue assertions,
  and cyclic/depth/disconnected/one-document graph edge cases. New internal cohesive
  classes/helpers obey400/200/50/3. No handler/ReadTarget or duplicate old case stays
  after the genuine replacement joins; preserve a complete six-case obligation map.
- AC-GH-002 / REQ-BC-023: a real fixture creates a private marker node and uniqueness
  constraint on a different private marker label, using the exact name the target
  will attempt. A real duplicate CREATE probe captures HTTP202 and actual native
  error code/message. Direct InitializeAsync rejects with ComparisonFailureException
  and code-only detail; DisposeAsync leaves the fixture marker and constraint
  intact. A fresh target and independently owned client then run the original
  setup-failure cut, Small with Warmup=0/Repetitions=1, against that same fixture.
  This preserves four supported failed
  cases with null Measurement/empty Samples, four unsupported null/empty cases,
  exact sanitized Setup detail, and no native message/private run ID/password.
  The independent fixture client owns bounded cleanup even after assertion errors.
- AC-GH-003 / REQ-BC-023: cleanup authority starts only after a complete parsed
  successful CREATE acknowledgement. Documented success may omit errors or carry
  an empty errors array; an explicit nonempty native error fails regardless of
  HTTP202. Validate claimed success against the real documented data.fields and
  data.values array shape for CREATE specifically: both arrays are empty,
  queryType is the exact string s, and bookmarks is an array containing only
  strings (empty allowed; opaque token contents unchanged). Other query paths
  use the same safe error parser without schema-only success requirements.
  Missing/malformed response/error/code/data/type/bookmarks must fail
  closed without echoing native messages and without granting authority. Parse
  once; dispose response/stream and any unreturned JsonDocument on every error.
  Existing successful query/session/driver/public ABI and corpus bytes remain.
  Lost acknowledgement may leave a private orphan; it never authorizes deleting
  a resource whose ownership is uncertain. This adds no general duplicate-label
  idempotence or lost-response/fault-recovery qualification.
- AC-GH-004 / REQ-BC-023: a genuine successful mini-run against a known private
  target uses the exact Small inputs and two repetitions. All four supported
  scenarios have12 successful measured samples per repetition; four unsupported
  scenarios have null measurements/empty samples. Before target disposal, query
  all28 distinct warmup/measured DocumentWrite IDs and exact stored JSON, plus
  total44 nodes including16 seeds. IDs follow the actual public Input formula.
  Only24 write attempts enter measured samples; all96 supported attempts enter
  CSV (97 lines including header). Preserve repetitions/revision and Markdown's
  guarantee wording. File writes/reads/cleanup use the actual report writer and
  real files. After successful disposal, zero nodes bear the private label and
  zero constraints match its exact private name; no label-token catalog assertion.
- AC-GH-005 / REQ-BC-023: another real mini-run uses warmup0/repetition1. Its existing
  synchronous progress observer modifies only that run's seeded label JSON to
  {} before PointRead measurements, through bounded real HttpClient.Send. Restore
  original seed values before DocumentWrite and in unconditional cleanup. PointRead
  retains12 PointReadMismatch failures, zero successes/useful throughput and all
  samples; other supported cases remain genuine and unsupported cases null/empty.
  The positive mini-run above proves the corresponding correct-payload case.
  No async blocking bridge or new product callback/session/target wrapper.
- AC-GH-006 / REQ-BC-023: source-owned protocol validation is checked against
  authentic successful and duplicate-error JSON captured from that real container,
  plus controlled in-memory structural variants for absent/malformed errors,
  missing/wrong data arrays and missing/null/wrong error codes. These are pure
  input-validation cases for the actual parser, not transport/server doubles.
  Error validation requires an object root and an absent or array errors member.
  A nonempty error array's first item must be an object with a nonempty string code
  whose four dot-separated components are ASCII identifiers (letter first, then
  letters/digits/underscore), with the first component Neo. A valid code fails with
  the existing Neo4j:code detail; every malformed shape fails with the fixed
  Neo4j:InvalidQueryResponse detail. Never echo arbitrary malformed code, message
  or body. Constraint validation checks errors before CREATE success shape.
  Duplicate critical properties fail with that same fixed detail, regardless of
  order/value: root errors/data/queryType/bookmarks, nested data fields/values,
  and code in the first native error item. A later empty errors array must never
  hide an earlier error. Unrelated provider metadata remains allowed and unchanged.
  An internal helper with a named, single ComparisonTests friend assembly may be
  used; public adapter ABI remains unchanged. Controlled variants cannot leave the
  test or become report/publication data. Test helpers own all real clients,
  responses, documents, streams, directories and tasks before outer Aspire disposal.
  Marker and constraint ownership are separately tracked only after acknowledged
  fixture setup. Attempt every acknowledged-owned cleanup even when setup, direct
  initialization, runner, progress callback or assertion fails. Use finite cleanup
  tokens independent of an already-cancelled scenario, await all actual work and
  dispose clients while preserving original plus cleanup failures. Target cleanup
  likewise attempts both owned node deletion and constraint drop and always client
  disposal; one failure must not skip the remaining owned cleanup or become success.
- AC-GH-007 / REQ-BC-023: complete reviewed source, ordinary enabled solution build,
  canonical formatter/governance/numeric rules and exact delivered-SHA GitHub
  TUnit/analyzer/unit/recovery/RF3 SDK/MCP/nine-engine comparisons pass without
  suppression, fakes, skips or reduced criteria. Required coverage collector,
  thresholds/no-decrease and fault/endurance evidence remain distinct mandatory
  gates. Source packets or historical main CI never qualify this candidate.

## Test matrix and methodology

| AC | Automated proof and meaningful assertions | Command / evidence |
|---|---|---|
| 001 | Three original pure TUnit tests; full method/input/assertion map, no obsolete doubles | GitHub complete UnitTests MTP after normal Release build; generated registration plus full source review |
| 002 | Neo4jHarnessRegression real native duplicate probe, direct target and runner setup failure, marker/constraint readback and code-only reports | GitHub ComparisonTests using the existing pinned Aspire container; exact run/job/SHA |
| 003 | Actual successful/error server responses and direct/runner ownership cases; pure structural validation variants in the real parser | Same real ComparisonTests command, source lifetime audit and enabled analysis |
| 004 | Real runner+target+independent readback before disposal, every expected write, exact CSV/sample/repetition checks, post-disposal absence | Same ComparisonTests; real file writer, genuine private output removed after assertions |
| 005 | Existing progress observer sends bounded real mutation/restore to own label; mismatch samples, failures, useful throughput, supported/unsupported state | Same ComparisonTests; positive004 supplies correct-payload proof |
| 006 | Authentic captured JSON and controlled malformed variants; no fake transport; unconditional real-resource cleanup | Same ComparisonTests plus full source/visibility/error audit |
| 007 | All mapped complete workflow suites, authentic SARIF/coverage/test/container artifacts and honest source/evidence links | gh workflow run ci.yml --repo managedcode/KeyLoad --ref main; inspect/download exact run artifacts |

Manual exceptions: lost HTTP acknowledgement and environmental cleanup/disposal
faults cannot be qualified by fabricated replies. Full source ownership audit is
required supplemental evidence; dedicated real fault qualification stays pending
and no recovery guarantee is claimed. Exact token/assertion/registration/policy,
secret-output and schema/topology preservation need lead/strongest source review.
Coverage numbers do not replace the positive/negative/native user flows above.

Compatibility: valid provider replies, public library CLR signatures, schema3,
wire/corpus/scenario/report values and measurement boundaries stay exact. Only
malformed acknowledgement rejection and failed-create cleanup authority tighten.
No product persisted migration; transient private benchmark resources only.
Rollback is a coherent reviewed genuine test/target unit, preserving cleanup safety
and meaningful original scenarios. Never restore prohibited doubles as a shortcut.
Pinned response/visibility details must be resolved before write-worker acceptance.
