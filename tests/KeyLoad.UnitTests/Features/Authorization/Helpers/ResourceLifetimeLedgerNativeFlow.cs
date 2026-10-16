namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeLedgerNativeFlow
{
    internal static async Task ExecuteAsync(SafeProjectorOperationDatabase owner)
    {
        var state = await SafeProjectorOperationSeed.CreateAsync(owner);
        var other = ResourceLifetimeLedgerNativeData.OtherResource(owner, state.Resource);
        await Assert.That(other.Result.Error).IsNull();
        var lifetime = ResourceLifetimeLedgerNativeData.Lifetime(owner, state.ResourceCommand, state.Resource);
        var otherDefinition = other.Result.Get<ResourceDefinition>();
        var otherLifetime = ResourceLifetimeLedgerNativeData.Lifetime(owner, other, otherDefinition);
        SafeProjectorOperationCommand? repair = null;
        var released = await ResourceLifetimeLedgerNativeSession.RunAsync(owner, async (workOwner, ledger, parent, budget, expiry) =>
        {
            await ResourceLifetimeLedgerNativeBusiness.InitialAsync(owner, workOwner, ledger,
                parent, budget, expiry, lifetime);
            repair = ResourceLifetimeLedgerNativeData.Repair(owner, state.Reader);
            await Assert.That(repair.Result.Error).IsNull();
            await ResourceLifetimeLedgerNativeBusiness.RepairedAsync(owner, workOwner, ledger,
                parent, budget, expiry, lifetime, otherLifetime);
        });
        _ = Assert.ThrowsExactly<ObjectDisposedException>(() => _ = released.RetainedEntries);
        _ = await ResourceLifetimeLedgerNativeSession.RunAsync(owner, async (_, ledger, parent, budget, _) =>
        {
            await Assert.That(ReferenceEquals(ledger, released)).IsFalse();
            await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger);
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerNativeSession.Read(owner, budget, parent),
                    ResourceLifetimeLedgerNativeData.Expected(owner));
                await ResourceLifetimeLedgerNativeAssertions.RetainedAsync(ledger, lifetime);
            });
        });
        await SafeProjectorOperationReplay.AllAsync(owner,
            [state.ResourceCommand, state.PrincipalCommand, state.Seed, other, repair!], ResourceLifetimeLedgerNativeData.RepairEpoch);
    }
}
