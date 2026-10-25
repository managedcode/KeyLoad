namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerFlow
{
    internal static async Task ExecuteAsync(SafeProjectorOperationDatabase owner)
    {
        var state = await SafeProjectorOperationSeed.CreateAsync(owner);
        var other = ResourceLifetimeLedgerData.OtherResource(owner, state.Resource);
        await Assert.That(Errors.Code(other.Result.Problem).Value).IsNull();
        var lifetime = ResourceLifetimeLedgerData.Lifetime(owner, state.ResourceCommand, state.Resource);
        var otherDefinition = other.Result.Get<ResourceDefinition>();
        var otherLifetime = ResourceLifetimeLedgerData.Lifetime(owner, other, otherDefinition);
        SafeProjectorOperationCommand? repair = null;
        var released = await ResourceLifetimeLedgerSession.RunAsync(owner, async (workOwner, ledger, parent, budget, expiry) =>
        {
            await ResourceLifetimeLedgerBusiness.InitialAsync(owner, workOwner, ledger,
                parent, budget, expiry, lifetime);
            repair = ResourceLifetimeLedgerData.Repair(owner, state.Reader);
            await Assert.That(Errors.Code(repair.Result.Problem).Value).IsNull();
            await ResourceLifetimeLedgerBusiness.RepairedAsync(owner, workOwner, ledger,
                parent, budget, expiry, lifetime, otherLifetime);
        });
        _ = Assert.ThrowsExactly<ObjectDisposedException>(() => _ = released.RetainedEntries);
        _ = await ResourceLifetimeLedgerSession.RunAsync(owner, async (_, ledger, parent, budget, _) =>
        {
            await Assert.That(ReferenceEquals(ledger, released)).IsFalse();
            await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger);
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                await SafeProjectorOperationSeed.EqualAsync(ResourceLifetimeLedgerSession.Read(owner, budget, parent),
                    ResourceLifetimeLedgerData.Expected(owner));
                await ResourceLifetimeLedgerAssertions.RetainedAsync(ledger, lifetime);
            });
        });
        await SafeProjectorOperationReplay.AllAsync(owner,
            [state.ResourceCommand, state.PrincipalCommand, state.Seed, other, repair!], ResourceLifetimeLedgerData.RepairEpoch);
    }
}
