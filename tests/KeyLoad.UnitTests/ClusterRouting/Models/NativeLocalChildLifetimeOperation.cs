using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeLocalChildLifetimeOperation(NativeRequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, NativeRequestWorkLease Parent,
    ReadExecutionBudget Budget, GrainRequestCodec Codec, DateTimeOffset Expiry, CancellationToken Token);
