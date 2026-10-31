using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using P = KeyLoad.FunctionalTests.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerBusiness
{
    internal static async Task InitialAsync(SafeProjectorOperationDatabase owner, RequestWorkOwner workOwner,
        ResourceLifetimeWorkResultRetention ledger, RequestWorkLease parent, ReadExecutionBudget budget,
        DateTimeOffset expiry, ResourceLifetime lifetime)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var page = ResourceLifetimeLedgerSession.Read(owner, budget, parent);
            await SafeProjectorOperationSeed.EqualAsync(page, ResourceLifetimeLedgerData.Expected(owner));
            await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger, lifetime);
            await ResourceLifetimeLedgerAssertions.HealthyChildAsync(owner,
                await ResourceLifetimeLedgerChild.ExecuteAsync(owner, workOwner, ledger, expiry, P.Collection));
            await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger, lifetime);
            await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerSession.Read(owner, budget, parent),
                ResourceLifetimeLedgerData.Expected(owner));
            var denied = await ResourceLifetimeLedgerChild.ExecuteAsync(owner, workOwner, ledger,
                expiry, ResourceLifetimeLedgerData.Other);
            await ResourceLifetimeLedgerAssertions.RefusedAsync(denied, ErrorCode.PermissionDenied);
            await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger, lifetime);
        });
    }

    internal static async Task RepairedAsync(SafeProjectorOperationDatabase owner, RequestWorkOwner workOwner,
        ResourceLifetimeWorkResultRetention ledger, RequestWorkLease parent, ReadExecutionBudget budget,
        DateTimeOffset expiry, ResourceLifetime lifetime, ResourceLifetime otherLifetime)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var healthy = await ResourceLifetimeLedgerChild.ExecuteAsync(owner, workOwner, ledger,
                expiry, ResourceLifetimeLedgerData.Other);
            await Assert.That(healthy.Error).IsNull();
            await SafeProjectorOperationSeed.EqualAsync(healthy.Page,
                new QueryPage([], null, owner.Store.Position, ResourceLifetimeLedgerData.FullScan));
            await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger, lifetime, otherLifetime);
            await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerSession.Read(owner, budget, parent),
                ResourceLifetimeLedgerData.Expected(owner));
        });
    }
}
