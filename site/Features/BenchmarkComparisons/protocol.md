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

`results-view.mjs` renders one section per data type with the same comparison table: the database column stays pinned, one column per case, and each cell shows a 1-node line and a 3-node line with operations per second (records per second for loads), a bar scaled to the best result of that case in the type, and the 3-node change against 1 node. The index build is measured once, so it shows seconds and treats the shortest time as best. Every declared database of the type has a row, KeyLoad first.

- A failed case reads "Failed" with the first line of its error; the full error stays in the line title and the recorded-values table. A case the job never reached after a job-level error reads "Failed" with that job error.
- A database without a 3-node result shows only its 1-node line and reads "Single node" beside its name.
- A database without any result file, or a case absent from its result, reads "No data" with the reason. Missing values never become zero.

A collapsed recorded-values table per type lists every case of every job with status, operations, errors, seconds, operations per second, p50/p95/p99 latency per call, units per call, error, image and run link. The page links the GitHub Actions runs and source commits named in the results and the merged file.

`results-controls.mjs` is the only benchmark browser module. It shows the data type filter, hides the other types' sections and shades the edges of horizontally scrolled tables. Without JavaScript every section stays visible.

## Tests

`tests/KeyLoad.WebsiteTests` runs the real merge script and builder through Node with artifact folders from its `Fixtures/Results` folder and checks the merged file and the rendered rows, columns and values. These values are test data and never published.
