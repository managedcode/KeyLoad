# ADR-145: No request throttling

Status: Accepted by owner direction 2026-10-24. In source; Linux qualification pending.

## Decision

KeyLoad does not throttle its callers. No node, tenant, principal, connection or
silo limit counts requests, commands or connections in order to refuse or queue
them, and no pool reserves modeled bytes for data, control or ingress work.
Requests are not sorted into data and control lanes for capacity; Orleans
schedules every accepted operation on its connection grain and the grains it calls.

Two kinds of limits remain:

- Request body size. `KeyLoad:ServerExecution:MaximumBodyBytes` is one Kestrel
  limit for every route, including MCP (default and maximum 33,554,432 bytes,
  32 MiB). An MCP POST whose declared `Content-Length` exceeds it is rejected
  before the body is read. The identity middleware rewrites a Kestrel 413 into a
  `ResourceExhausted` problem (HTTP 429).
- Protocol and transport bounds. `DatabaseLimits` sets no per-operation data
  bound, and grain request streams carry no reply byte or frame cap
  ([ADR-149](ADR-149-no-operation-limits.md) removes them); a short-profile grain
  request stream has the fixed two-chunk shape
  (`GrainRequestStreamProtocol.MaximumChunks`); Orleans keeps its default message
  size limits; the MCP JSON token and property limits; the MCP canonical payload
  ceiling, equal to the server body limit; the classified MCP reply ceilings
  `McpExecutionOptions.MaximumControlReplyBytes` and `MaximumDataReplyBytes`; and
  the connection idle timeout. Internal request envelopes carry no expiry.

Engine-internal resource planning is not request throttling and remains: ANN
resident-memory and disk planning (`AnnExecutionOptions.MaximumResidentBytes`,
`MaximumDiskBytes` and read reservations), text-search physical inventory and
disk bounds (`TextExecutionOptions` files, directories, generations and disk
bytes), and cache memory budgets (`CacheMemoryLimits`) and cache read permits.

The database engine does not cap concurrent work either. Each analytical read
(SQL, AST, live query, text/vector/hybrid search) holds one counted reservation
on its engine so release can be proved, but the count never refuses a read. The
change outbox has no producer quota or projection progress reserve: producers
are never refused because projection consumers lag, and retained outbox size
depends only on consumer progress and purge. `DatabaseLimits` no longer has
`WriterQueueCapacity`, `MaxConcurrentQueries`, `MaxOutboxRecords`,
`MaxOutboxBytes`, `ReservedOutboxRecords` or `ReservedOutboxBytes`, and a
projection consumer no longer records a progress reservation cut.

Full-text search counts its reader and maintenance leases so shutdown can join
them, but no count refuses work: `TextExecutionOptions.MaximumActiveLeases` is
gone, and online, selected and incremental index readers and maintenance
sessions have no count limit. Two structural exclusions remain. The bootstrap
text projection, used only until a maintained index is current, leases each
generation slot to one reader at a time because a reader that finds a stale
generation retires its files. An ANN generation owner refuses a public read with
`ResourceExhausted` when its owner gate is busy (another read's reservation step
or a maintenance stage) instead of blocking the read's thread behind that work.

Identity collisions are conflicts, not capacity. A duplicate active request ID on
one connection, or a duplicate request-work identity in the silo registry, fails
with `Conflict` ("identity is already active"). Postgres wire cancellation
registrations have no count limit; a cancellation identity collision fails with
`Conflict`.

Comparisons measure KeyLoad without an admission profile. KeyLoad nodes in a
benchmark run with no request throttling, the same as the other databases' clients.

## Consequences

- Overload shows up as latency, not as refusals. A caller that sends more work
  than the cluster can serve waits longer; it does not receive `ResourceExhausted`
  because a node, tenant or principal pool is full.
- Operators size hardware (silos, CPU, memory, disk) for their load instead of
  tuning admission pools. The protocol and transport bounds above remain;
  ADR-149 removes the configured per-operation data limits and the grain reply
  and frame caps.
- Cancellation, shutdown and recovery do not depend on reserved control capacity.
  Each operation carries its own cancellation, and shutdown closes new work and
  joins running operations before storage closes.
- The configuration sections `KeyLoad:HttpAdmission`, `KeyLoad:CommandAdmission`
  and `KeyLoad:McpMemory`, the `GrainRoutingOptions` settings
  `MaximumRequestProducers`, `MaximumConnections` and
  `MaximumConnectionOperations`, the ANN per-generation and per-slot reader caps
  and the ANN operation-count gate are removed.
- The admission status surface is removed: `GET /admin/admission`,
  `KeyLoadClient.AdmissionStatusAsync`, the MCP tool `keyload_admin_admission`,
  the `Admission` read kind (`NodeStatus` keeps its wire value 19),
  `AdminNodeSnapshot.Admission` and the admin dashboard admission card.
