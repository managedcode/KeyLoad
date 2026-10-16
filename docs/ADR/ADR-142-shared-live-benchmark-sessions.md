# ADR-142: One live benchmark session per database and native topology

Date: 2026-10-17. Status: owner-selected decision; implementation and Linux qualification pending.

## Context

The previous benchmark planner created 1,898 workers, repeatedly built the same
runner and prepared infrastructure for separate operation cells. Actual runs
failed before measurement while consuming many hours in preparation. The owner
requires two measurement jobs per database, shared infrastructure across many
real tests, a measured initial million records and no warmup or per-case reset.
The owner also explicitly requires removal of the superseded pipeline code.

## Decision

One Linux runner owns one database's native one-node or three-node topology for
the entire session. Aspire starts and disposes that topology once. Separate
native TUnit cases share its fixture; the first performs and measures the actual
million-record load. Later operation and mixed tests use the resulting live
state, with bounded clients, real acknowledgements, original operation samples
and exact final-state oracles. The canonical inventory and unsupported native
capabilities are explicit; a document-only implementation does not qualify the
complete database/model comparison requirement.

One preparation job builds the solution, native images and compiled runner
bundle. Workers import and hash-check same-source/run/attempt images, DLLs/PDBs,
NuGet assets and required Aspire native tools without their own restore/build.
The preparation and each worker retain original reports. Aggregation requires
all 22 database/topology workers, native test outcomes, artifact identities,
source identity, measured counts and matching operation statistics. Failed
original cases retain null metrics; missing or corrupted originals fail closed.
Website remains an independently qualified downstream consumer.

No warmup, hidden reseed, per-test database restart, retry that improves samples,
legacy producer, report compatibility fallback or fabricated unavailable report
is part of this design. Reusable native operations and transport retain their
actual contracts. Retired planners, dispatchers, schemas, readers and exclusive
fixtures are removed together. Immutable historical measurements stay historical.

## Consequences and qualification

The measurement matrix is 11 databases × two topologies = 22 jobs. Shared
preparation, aggregation and Website dispatch make 25 comparison-flow jobs. The
existing functional RF3 heavy-load correctness job remains separately present
until its owner moves it without losing acceptance coverage. A job failure may
leave later prerequisites failed; original case outcomes must show this honestly.

[SessionExecution](../Features/BenchmarkComparisons/SessionExecution.md) owns
REQ/AC-BSESSION-001..008, adapter inventory, disjoint implementation ownership,
native regression mappings and the concrete Linux completion gates. Exact-source
build, formatting, contract tests, real native sessions, RF3 SDK/MCP acceptance,
complete supported-family accounting and website provenance remain mandatory.
Neither this decision nor a copied binary bundle proves execution or performance.
