# Website benchmark results protocol

Benchmarks runs one GitHub Actions job per database, data type and node count. Each job starts its database through Aspire, runs the shared scenario of its data type on that type's standard dataset and uploads its result file, also when the job fails. KeyLoad runs every type's scenario on the same dataset as the specialist databases, so every type compares KeyLoad with those databases.

## Result file

The harness names each file after its own content, `<database slug>-<type>-n<nodes>.json`, for example `postgresql-pgvector-vector-n3.json`.

```json
{ "database": "KeyLoad", "type": "graph", "nodes": 3, "image": "…", "commit": "…", "runId": 123,
  "startedAt": "…", "finishedAt": "…", "records": 1000000, "operations": 100000, "clients": 16, "error": null,
  "cases": [ { "id": "neighbors", "status": "passed", "operations": 100000, "errors": 0, "seconds": 41.2,
               "opsPerSecond": 2427, "p50Ms": 5.1, "p95Ms": 11.0, "p99Ms": 19.4, "batchSize": 1, "error": null } ] }
```

`results-contract.mjs` owns the shape: exact field names, the data types with their case ids and databases, node counts 1 or 3, ISO timestamps, positive `records`, `operations` and `clients`, and unique case ids. `image` and `commit` are strings or null, `runId` a non-negative integer or null, and `finishedAt` an instant or null; an empty or absent value is shown as absent. A job-level `error` is null or the error that stopped the job before its remaining cases. Every case records non-negative counts and measurements and a positive `batchSize`, the units per call; latencies are per call. A `passed` case has a null `error`; a `failed` case carries its first error.

| Type | Cases | Databases besides KeyLoad |
| --- | --- | --- |
| documents | load, point-read, range-query, update, delete, mixed | MongoDB, SurrealDB |
| key-value | load, get, set, delete, mixed | Redis |
| relational | load, pk-select, range-select, update, delete, mixed | PostgreSQL + pgvector, SQL Server, MySQL |
| graph | load, neighbors, traverse | Neo4j, HelixDB, SurrealDB |
| vector | load, exact-top10 | Qdrant, PostgreSQL + pgvector |
| search | load, index, term-query, and-query | OpenSearch |
| time-series | load, range-read, aggregate, windows | TimescaleDB, InfluxDB |
| analytics | load, group-by, filtered-aggregate | ClickHouse |
| queue | cycle | RabbitMQ |
| events | load, append, read, mixed | KurrentDB |

## Merge and build

```sh
node site/scripts/results.mjs --input=results --output=site-results/results.json
node site/scripts/build.mjs --results=site-results/results.json --output=_site --site-revision=<40-hex revision>
node site/scripts/build.mjs --results=none --output=_site --site-revision=<40-hex revision>
```

`results.mjs` walks the downloaded artifacts and reads every file named like a result file at any depth; job test logs beside them are not result files. Each result must match the shape and its own file name. The merge fails when there is no result file, when a file is not valid JSON or breaks the shape, when the tree holds a symbolic link, and when two files describe the same database, type and node count. It writes `{ "results": [...] }` sorted by type, database and node count and never overwrites an existing file.

The builder validates the merged file with the same contract, renders the benchmark section into static HTML and publishes the merged file unchanged as `data/results.json`. `--results=none` builds the page without figures and states that no results are available yet. The output must be a new directory outside the site source; a failed build removes it. The builder uses Node built-ins only. Authored browser JavaScript stays within 40,960 gzip bytes and CSS within 20,480.

## Rendering

`results-view.mjs` renders a global summary and one detailed comparison table per data type. The browser selects one table at a time. All types shows each declared database exactly once, including KeyLoad exactly once. Its shared columns are Load (records/s), Write, Read, Search, Delete and Mixed (ops/s). The type tabs use their own original case columns, including Index build in seconds for Search.

`results-summary.mjs` derives each global rate from the original result files: sum of completed operations divided by sum of measured seconds for the recorded cases in that metric and node count. It does not average rounded rates. Each topology line shows the case count used. Different databases can cover different workloads; the type tables retain the matched scenarios and exact recorded values.

The global metrics map load to Load; update/set/append to Write; point-read/get/pk-select/neighbors/range-read/read to Read; range-query/range-select/traverse/exact-top10/term-query/and-query/aggregate/windows/group-by/filtered-aggregate to Search; delete to Delete; and mixed/cycle to Mixed. A queue cycle is not split into invented independent read or write rates. Index build is a one-time duration and remains in the Search type table.

Only recorded jobs contribute to a summary. A recorded job's failed case or job-level error makes that metric read Failed with its error; an absent case within a recorded job reads No data. A metric without a recorded case or a positive measured duration has no number. A dash means the database's declared types have no case for that metric. Missing jobs do not become zero. All original cases and errors remain in the recorded-values tables and the unchanged downloadable results file.

Every comparison explicitly labels its 1-node and 3-node values. The 3-node speed change says "vs 1 node" and the global summary shows it only when both topologies cover the same original cases. Global bars scale to the fastest summary rate of that metric; type-table bars scale to the best recorded value of the original case. Shorter Index build durations are faster.

A database without a 3-node result shows only its 1-node line and reads Single node. A database with no results reads No data. Each table has a collapsed recorded-values table listing original case status, counts, duration, throughput, latency, batch size, error, image and run. The global recorded-values table contains all types; type tables contain only their own cases.

`results-controls.mjs` selects the summary or original type table and shades horizontally scrolled table edges. Each table starts with its first metric's fastest 1-node value. Selecting a metric always sorts fastest first, using shorter durations for Index build. Failed, missing and inapplicable values follow measured values. The sort basis is visible above each table. Without JavaScript all tables remain visible in their initial speed order.

## Tests

`tests/KeyLoad.WebsiteTests` runs the real merge script and builder through Node with artifact folders from its `Fixtures/Results` folder and checks the merged file and the rendered rows, columns and values. These values are test data and never published.
