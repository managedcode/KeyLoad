using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed record BoundLeaseOperation(RequestWorkOwner Owner,
    ResourceLifetimeWorkResultRetention Ledger, RequestWorkLease Parent,
    ReadExecutionBudget Budget, CancellationToken Token);
