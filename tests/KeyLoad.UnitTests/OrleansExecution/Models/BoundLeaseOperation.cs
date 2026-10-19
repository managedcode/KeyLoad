using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed record BoundLeaseOperation(RequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, RequestWorkLease Parent,
    ReadExecutionBudget Budget, DateTimeOffset Expiry, CancellationToken Token);
