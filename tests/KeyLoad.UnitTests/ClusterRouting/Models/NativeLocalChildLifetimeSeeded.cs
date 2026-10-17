using KeyLoad.UnitTests.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeLocalChildLifetimeSeeded(RequestCqrsCohortNativeNode Leader,
    RequestCqrsCohortNativeNode Other, PhysicalShardRecord PhysicalOwner,
    ReplicatedOperation Write, CommitReceipt Receipt,
    Dictionary<string, (long Position, string[] Records)> Images);
