# Orleans foundation options

The owner requires Orleans as the complete cluster foundation, TUnit, Docker/Aspire and real SDK/MCP clients. Keeping DotNext or a primary-silo development membership table would violate the requested topology/availability. Moving journals into ordinary migratable activations would violate node ownership.

Chosen direction: fixed RF3 node-owned durable replicas invoked by Orleans per-silo Grain Services, unique request grains, distributed directory and activation repartitioning. Membership must bootstrap through the same replica protocol before public requests become ready. Signed HTTP may discover a silo address only; all replication and database work uses Orleans. Read-only review must confirm lifecycle behavior before transport implementation. Durable log/snapshot metadata reuse real atomic storage and bounded canonical snapshots.

Independent work: test-framework conversion can proceed while bootstrap is reviewed and durable-log contracts are written. Shared contracts, host composition and central package/build/docs changes remain lead-owned. Tests execute only in CI; existing successful CI is the historical baseline, not proof of this migration.
