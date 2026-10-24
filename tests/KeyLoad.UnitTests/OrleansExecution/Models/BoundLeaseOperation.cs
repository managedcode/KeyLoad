using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record BoundLeaseOperation(RequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, RequestWorkLease Parent,
    ReadExecutionBudget Budget, DateTimeOffset Expiry, CancellationToken Token);
