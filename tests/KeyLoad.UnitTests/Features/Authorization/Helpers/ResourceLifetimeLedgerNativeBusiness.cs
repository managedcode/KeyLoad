using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeLedgerNativeBusiness
{
    internal static async Task InitialAsync(SafeProjectorOperationDatabase owner, NativeRequestWorkOwner workOwner,
        ResourceLifetimeWorkResultRetention ledger, NativeRequestWorkLease parent, ReadExecutionBudget budget,
        DateTimeOffset expiry, ResourceLifetimeV1 lifetime)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var page = ResourceLifetimeLedgerNativeSession.Read(owner, budget, parent);
            await SafeProjectorOperationSeed.EqualAsync(page, ResourceLifetimeLedgerNativeData.Expected(owner));
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime);
            var outputBytes = budget.MeasureResult(page);
            var tight = checked((int)(NativeSerialization.Measure(lifetime) + outputBytes
                - ResourceLifetimeLedgerNativeData.OneByte));
            await ResourceLifetimeLedgerNativeAssertions.HealthyChildAsync(owner,
                await ResourceLifetimeLedgerNativeChild.ExecuteAsync(owner, workOwner, ledger, expiry, P.Collection));
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime);
            var refused = await ResourceLifetimeLedgerNativeChild.ExecuteAsync(owner, workOwner, ledger,
                expiry, P.Collection, tight);
            await ResourceLifetimeLedgerNativeAssertions.RefusedAsync(refused, ErrorCode.BudgetExceeded);
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime);
            await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerNativeSession.Read(owner, budget, parent),
                ResourceLifetimeLedgerNativeData.Expected(owner));
            var denied = await ResourceLifetimeLedgerNativeChild.ExecuteAsync(owner, workOwner, ledger,
                expiry, ResourceLifetimeLedgerNativeData.Other);
            await ResourceLifetimeLedgerNativeAssertions.RefusedAsync(denied, ErrorCode.PermissionDenied);
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime);
        });
    }

    internal static async Task RepairedAsync(SafeProjectorOperationDatabase owner, NativeRequestWorkOwner workOwner,
        ResourceLifetimeWorkResultRetention ledger, NativeRequestWorkLease parent, ReadExecutionBudget budget,
        DateTimeOffset expiry, ResourceLifetimeV1 lifetime, ResourceLifetimeV1 otherLifetime)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var healthy = await ResourceLifetimeLedgerNativeChild.ExecuteAsync(owner, workOwner, ledger,
                expiry, ResourceLifetimeLedgerNativeData.Other);
            await Assert.That(healthy.Error).IsNull();
            await SafeProjectorOperationSeed.EqualAsync(healthy.Page,
                new QueryPage([], null, owner.Store.Position, ResourceLifetimeLedgerNativeData.FullScan));
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime, otherLifetime);
            await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerNativeSession.Read(owner, budget, parent),
                ResourceLifetimeLedgerNativeData.Expected(owner));
        });
    }
}
