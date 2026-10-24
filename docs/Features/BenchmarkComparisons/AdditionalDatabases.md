# SQL and analytical comparison targets

The owner authorized TimescaleDB, SQL Server, MySQL, ClickHouse and InfluxDB as
benchmark targets on 2026-10-24. They extend the current session cohort to sixteen
systems and 32 one/three-node rows. The benchmark projects alone own the drivers,
native operation adapters and containers. KeyLoad's server, SDK and storage do not
adopt these engines or their drivers.

## Implemented operation contracts

| Target | Native storage and transport | Current session operations |
|---|---|---|
| TimescaleDB | A real hypertable through Npgsql; deterministic per-document timestamps and complete text payloads | Load, numeric index, point reads, compound predicates, update/delete, mixed CRUD and ordered final readback |
| SQL Server | A unique database, binary-collated primary key and parameterized Microsoft.Data.SqlClient commands | The complete document session; delayed durability must be disabled |
| MySQL | A unique database, InnoDB table, binary-collated primary key and parameterized MySqlConnector commands | The complete document session; `innodb_flush_log_at_trx_commit=1` and `sync_binlog=1` are checked |
| ClickHouse | A unique database, MergeTree and bounded native HTTP queries | The complete document session; acknowledged asynchronous inserts wait for actual flush, inserts request filesystem synchronization, and native update/delete mutations wait for completion |
| InfluxDB | A unique database/table, file object store, native line protocol and parameterized SQL over HTTP | Load, point reads, compound predicates and full final readback. Tags retain document identity; fields retain the exact body and number. A deterministic timestamp identifies an update to the same point. Core has no row delete operation, so mixed CRUD is unavailable. Index construction is unavailable because Core manages its indexes during ingestion. |

These document sessions do not qualify time-window aggregates, analytical scans,
vector search or any other model family. The existing separate TimescaleDB
TimeSeries targets and their open scale requirements under ADR-050/059 remain
distinct. A hypertable document measurement cannot substitute for those results.

Each session still measures its initial million records and at least 100,000
operations in each applicable lane. Complete ordered readback verifies actual
identities and bodies. InfluxDB's final readback verifies the unchanged corpus
after its supported operations; it does not invent a mixed mutation result.

## Deployment and qualification limits

The added native deployments currently implement one node. TimescaleDB, SQL Server,
MySQL and ClickHouse three-node rows are explicitly unavailable because this
benchmark does not yet implement or qualify their replicated deployment and
acknowledgement contracts. This is an implementation gap, not a claim that these
databases lack replication. InfluxDB 3 Core is a single-node product; Enterprise
clustering is outside this comparison. Separate standalone servers never count as
replicas.

Aspire owns the official pinned container images, anonymous session volumes,
readiness and joined cleanup. Native SQL readiness opens a real provider
connection and executes `SELECT 1`; HTTP targets use their server health endpoints.
SQL Server uses Developer edition for nonproduction benchmarks and requires the
Linux x86-64 runner. Its local execution on ARM is not qualified.

`AdditionalDatabaseTests` exercises real native operations, complete-body updates,
missing reads, independently owned clients, cancellation followed by healthy
reads, supported deletes, compound predicates and ordered readback through a
separate Aspire lifecycle. The Linux one-node workers run it before starting the
fresh measured session. These bounded correctness cases provide no performance
figures. Complete Debug build, actual native tests, Linux million-record sessions,
resource observations and authenticated aggregate/site admission remain separate
open gates until their original results exist. Local development verification on
2026-10-24 passed the complete Debug solution build with warnings as errors,
formatter verification, governance, nine parser/admission scenarios (131 altered
inputs rejected) and four genuine one-node adapter cases: TimescaleDB, MySQL,
ClickHouse and InfluxDB. SQL Server execution remains pending on Linux x86-64;
these small correctness cases do not qualify million-record performance or
replication.

## Driver adoption

The owner explicitly approved benchmark-only Microsoft.Data.SqlClient 7.1.1 and
MySqlConnector 2.6.2 after review of versions, original MIT licenses, parameterized
ADO.NET APIs, asynchronous cancellation and disposal. Both versions are centrally
pinned and referenced only by `KeyLoad.Comparisons`. Native pools and command
deadlines use the existing validated comparison execution policy. ClickHouse and
InfluxDB use the existing bounded HTTP transport; TimescaleDB reuses Npgsql.

Publisher sources:

- [SqlClient package](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.1)
  and [original license](https://github.com/dotnet/SqlClient/blob/main/LICENSE).
- [MySqlConnector package](https://www.nuget.org/packages/MySqlConnector/2.6.2),
  [original license](https://github.com/mysql-net/MySqlConnector/blob/master/LICENSE)
  and [cancellation contract](https://mysqlconnector.net/overview/command-cancellation/).
- [InfluxDB native write API](https://docs.influxdata.com/influxdb3/core/api/write-data/)
  and [SQL query API](https://docs.influxdata.com/influxdb3/core/api/query-data/).
- [SQL Server container platform requirements](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-docker-container-deployment?view=sql-server-ver17).

The owner separately approved pinning `System.Security.Cryptography.Pkcs` 10.0.13
in the benchmark project after restore selected vulnerable transitive version
9.0.18 and failed NU1902/NU1903. This explicit reference keeps the selected SqlClient
dependency graph on the fixed Microsoft MIT package. Audit and warning gates stay
enabled.
