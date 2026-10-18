using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeBoundLeaseOperation(NativeRequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, NativeRequestWorkLease Parent,
    ReadExecutionBudget Budget, DateTimeOffset Expiry, CancellationToken Token);
