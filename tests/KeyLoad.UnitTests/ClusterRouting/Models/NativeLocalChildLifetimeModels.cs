using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.UnitTests.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeLocalChildLifetimeSeeded(RequestCqrsCohortNativeNode Leader,
    RequestCqrsCohortNativeNode Other, PhysicalShardRecord PhysicalOwner,
    ReplicatedOperation Write, CommitReceipt Receipt,
    Dictionary<string, (long Position, string[] Records)> Images);

internal sealed record NativeLocalChildLifetimeOperation(NativeRequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, NativeRequestWorkLease Parent,
    ReadExecutionBudget Budget, GrainRequestCodec Codec, DateTimeOffset Expiry, CancellationToken Token);

internal sealed record NativeLocalChildLifetimeInput(PartitionQueryOwnedLeafRequestV2 Request,
    DecodedGrainRequest Verified);
